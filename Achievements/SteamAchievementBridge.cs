using System.Reflection;
using System.Runtime.ExceptionServices;
using DeadCellsMultiplayerMod.PortableCore;
using Steamworks;

namespace DeadCellsMultiplayerMod;

internal static class SteamAchievementBridge
{
    const uint DeadCellsAppId = 588650;
    const string HookTypeName = "dc.steam.Hook__Api";
    const string HookEventName = "setAchievement";

    static bool _installed;
    static bool _cheatsLatched;
    static bool _cheatsLogged;
    static readonly HashSet<string> _sent = new(StringComparer.Ordinal);
    static readonly HashSet<string> _shared = new(StringComparer.Ordinal);

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        try
        {
            var hookType = typeof(dc.String).Assembly.GetType(HookTypeName);
            var evt = hookType?.GetEvent(HookEventName, BindingFlags.Public | BindingFlags.Static);
            var handlerType = evt?.EventHandlerType;
            var method = typeof(SteamAchievementBridge).GetMethod(nameof(OnSetAchievement), BindingFlags.NonPublic | BindingFlags.Static);
            if (evt == null || handlerType == null || method == null)
            {
                LobbySession.Log?.Warning("[Achievements] {Type}.{Event} not found, Steam achievements stay disabled", HookTypeName, HookEventName);
                return;
            }

            var handler = Delegate.CreateDelegate(handlerType, method, false);
            if (handler == null)
            {
                LobbySession.Log?.Warning("[Achievements] {Type}.{Event} signature mismatch, Steam achievements stay disabled", HookTypeName, HookEventName);
                return;
            }

            evt.AddEventHandler(null, handler);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Achievements] Hook install failed: {Message}", ex.Message);
        }
    }

    static bool OnSetAchievement(Delegate orig, dc.String id)
    {
        object? result;
        try
        {
            result = orig.DynamicInvoke(id);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        OnLocalAchievement(id?.ToString());
        return result is true;
    }

    public static void OnRemoteAchievement(AchievementMessage message)
    {
        if (!MultiplayerSettingsStorage.SteamAchievements || !MultiplayerSettingsStorage.ShareAchievements)
            return;
        Unlock(message.Name, message.PlayerId);
    }

    static void OnLocalAchievement(string? apiName)
    {
        if (!AchievementWire.IsValidName(apiName) || CheatsBlock())
            return;

        if (MultiplayerSettingsStorage.SteamAchievements)
            Unlock(apiName!, 0);

        try
        {
            var net = LobbySession.NetRef;
            if (net != null && net.IsAlive && _shared.Add(apiName!))
                net.SendAchievement(apiName!);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Achievements] Share failed: {Message}", ex.Message);
        }
    }

    static void Unlock(string apiName, int fromPlayer)
    {
        try
        {
            if (!AchievementWire.IsValidName(apiName) || _sent.Contains(apiName) || CheatsBlock())
                return;

            if (!ModEntry.EnsureSteamApiForNetworking("achievements"))
                return;

            var unlocked = ModEntry.RunSteamNetworkingSerialized(() =>
            {
                if (SteamUtils.GetAppID().m_AppId != DeadCellsAppId)
                    return false;
                if (SteamUserStats.GetAchievement(apiName, out var already) && already)
                    return true;
                return SteamUserStats.SetAchievement(apiName) && SteamUserStats.StoreStats();
            });

            if (unlocked)
                _sent.Add(apiName);
            LobbySession.Log?.Information("[Achievements] {Name} unlocked={Unlocked} from={From}", apiName, unlocked, fromPlayer == 0 ? "local" : fromPlayer);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Achievements] Unlock failed: {Message}", ex.Message);
        }
    }

    static bool CheatsBlock()
    {
        if (!DebugCheatsUsed())
            return false;
        if (!_cheatsLogged)
        {
            _cheatsLogged = true;
            LobbySession.Log?.Information("[Achievements] Blocked until restart: debug cheats were enabled this session");
        }
        return true;
    }

    static bool DebugCheatsUsed()
    {
        if (_cheatsLatched)
            return true;
        if (!MultiplayerSettingsStorage.IsDebugSectionEnabled)
            return false;

        var perk = MultiplayerSettingsStorage.DebugStartPerkId;
        _cheatsLatched =
            MultiplayerSettingsStorage.DebugPlayerImmortal ||
            MultiplayerSettingsStorage.DebugUseExplorersRune ||
            (!string.IsNullOrWhiteSpace(perk) &&
             !string.Equals(perk, MultiplayerSettingsStorage.NoStartPerkValue, StringComparison.OrdinalIgnoreCase));
        return _cheatsLatched;
    }
}

/*
 * ═══════════════════════════════════════
 *  STEAM ACHIEVEMENT BRIDGE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  DCCM при включённых модах подменяет steam.set_achievement / get_achievement заглушкой «true».
 *  В ref-сборке MDK нет dc.steam, поэтому хук dc.steam._Api_.setAchievement ставится рефлексией
 *  по рантайм-GameProxy (Hook__Api.setAchievement). Проверки игры (activeMods, allowAchievements,
 *  тренировка) стоят выше, в _Achievements, и остаются в силе.
 *  Своя ачивка: SteamAchievements → SetAchievement + StoreStats через Steamworks.NET мода
 *  (лок сетевых Steam-вызовов, только AppID 588650); в сессии один раз шлётся ACH напарникам.
 *  Чужая ACH: открывается у себя, если включены SteamAchievements и ShareAchievements.
 *  Уже открытая в Steam или отправленная за сессию ачивка повторно не шлётся.
 *  Debug-читы (бессмертие, руна, стартовый перк) хоть раз за сессию → ни выдачи, ни рассылки до перезапуска.
 *  get_achievement не трогается: открытость контента в игре остаётся как при DCCM.
 * ═══════════════════════════════════════
*/
