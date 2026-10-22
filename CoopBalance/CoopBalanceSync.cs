using dc;
using dc.en;
using dc.tool.atk;
using DeadCellsMultiplayerMod.PortableCore;
using Math = System.Math;

namespace DeadCellsMultiplayerMod;

internal static class CoopBalanceSync
{
    const long HostTickIntervalMs = 250;

    static bool _installed;
    static long _nextHostTickAt;
    static CoopDifficulty? _sentDifficulty;
    static double _sentMobDamage = double.NaN;
    static CoopDifficulty? _remoteDifficulty;
    static double _remoteMobDamage = 1.0;

    public static void Install()
    {
        if (_installed)
            return;
        Hook_Hero.applyAttackResult += Hook_Hero_applyAttackResult;
        _installed = true;
    }

    public static CoopDifficulty HostDifficulty => CoopBalance.Parse(MultiplayerSettingsStorage.CoopDifficulty);

    public static CoopDifficulty SessionDifficulty
    {
        get
        {
            var net = LobbySession.NetRef;
            if (net != null && net.IsAlive && !net.IsHost)
                return _remoteDifficulty ?? HostDifficulty;
            return HostDifficulty;
        }
    }

    public static bool HasHostDifficulty
    {
        get
        {
            var net = LobbySession.NetRef;
            return net == null || !net.IsAlive || net.IsHost || _remoteDifficulty.HasValue;
        }
    }

    public static int HostPlayerCount => 1 + Math.Max(0, NetNode.ConnectedClientCount);

    public static CoopBalanceValues PreviewValues(CoopDifficulty difficulty)
        => CoopBalance.For(difficulty, Math.Max(2, HostPlayerCount));

    static double ComputeHostMobDamage()
        => CoopBalance.Sanitize(CoopBalance.For(HostDifficulty, HostPlayerCount).MobDamage * MultiplayerSettingsStorage.MobsDamageMultiplier);

    public static double LocalMobDamage
    {
        get
        {
            var net = LobbySession.NetRef;
            if (net == null || !net.IsAlive)
                return 1.0;
            return net.IsHost ? ComputeHostMobDamage() : _remoteMobDamage;
        }
    }

    public static void CycleHostDifficulty()
    {
        var next = CoopBalance.Next(HostDifficulty);
        MultiplayerSettingsStorage.CoopDifficulty = CoopBalance.ToWire(next);
        LobbySession.Log?.Information("[CoopBalance] Host difficulty -> {Difficulty}", CoopBalance.ToWire(next));
        _nextHostTickAt = 0;
        TickHost();
    }

    public static void OnSettingsChanged()
    {
        _nextHostTickAt = 0;
    }

    public static void OnRemoteConnected()
    {
        _sentDifficulty = null;
        _sentMobDamage = double.NaN;
        _nextHostTickAt = 0;
    }

    public static void TickHost()
    {
        var now = Environment.TickCount64;
        if (now < _nextHostTickAt)
            return;
        _nextHostTickAt = now + HostTickIntervalMs;

        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive || !net.IsHost || NetNode.ConnectedClientCount <= 0)
        {
            _sentDifficulty = null;
            _sentMobDamage = double.NaN;
            return;
        }

        var difficulty = HostDifficulty;
        var damage = ComputeHostMobDamage();
        if (_sentDifficulty == difficulty && Math.Abs(_sentMobDamage - damage) < 0.0001)
            return;

        _sentDifficulty = difficulty;
        _sentMobDamage = damage;
        net.SendCoopBalance(CoopBalance.BuildLine(difficulty, damage));
        LobbySession.Log?.Information(
            "[CoopBalance] Sent {Difficulty} players={Players} mobDamage={Damage}",
            CoopBalance.ToWire(difficulty),
            HostPlayerCount,
            damage);
    }

    public static void OnHostLine(CoopDifficulty difficulty, double mobDamage)
    {
        var changed = _remoteDifficulty != difficulty;
        _remoteDifficulty = difficulty;
        _remoteMobDamage = CoopBalance.Sanitize(mobDamage);
        LobbySession.Log?.Information(
            "[CoopBalance] Host balance {Difficulty} mobDamage={Damage}",
            CoopBalance.ToWire(difficulty),
            _remoteMobDamage);
        if (changed)
            LobbySession.RequestLobbyMenuRefresh();
    }

    public static void Reset()
    {
        _remoteDifficulty = null;
        _remoteMobDamage = 1.0;
        _sentDifficulty = null;
        _sentMobDamage = double.NaN;
        _nextHostTickAt = 0;
    }

    static void Hook_Hero_applyAttackResult(Hook_Hero.orig_applyAttackResult orig, Hero self, AttackData a)
    {
        try
        {
            if (a != null && self != null && ReferenceEquals(self, ModEntry.me) && IsMobAttack(a))
            {
                var multiplier = LocalMobDamage;
                if (Math.Abs(multiplier - 1.0) > 0.0001)
                    a.finalDmg = CoopBalance.ScaleDamage(a.finalDmg, multiplier);
            }
        }
        catch
        {
        }

        orig(self, a);
    }

    static bool IsMobAttack(AttackData a)
    {
        Entity? entity = a.source ?? a.carrier;
        for (var depth = 0; depth < 4 && entity != null; depth++)
        {
            if (entity is Hero)
                return false;
            if (entity is Mob)
                return true;
            entity = entity.parent;
        }
        return false;
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP BALANCE SYNC — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Сложность выбирает хост (настройка CoopDifficulty), число игроков = 1 + подключённые клиенты.
 *  Здоровье мобов/боссов: только у хоста, через BossSyncHelpers.GetHpMultiplierForMob (клиент получает HP снапшотами).
 *  Урон мобов: каждый пир масштабирует удар по СВОЕМУ герою в Hero.applyAttackResult до вычитания life.
 *  Удар считается мобьим, если source/carrier или их parent (до 4 уровней) — Mob; герои и ловушки не трогаются.
 *  Хост раз в 250 мс сверяет (сложность, итоговый урон) с отправленным и шлёт BALANCE при изменении:
 *  смена сложности, ползунок урона, вход/выход игрока. Новый клиент → сброс, повторная отправка.
 *  Клиент без BALANCE получает урон ×1. Reset — при конце сессии.
 * ═══════════════════════════════════════
*/
