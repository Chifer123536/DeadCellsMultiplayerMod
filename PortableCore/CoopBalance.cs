using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal enum CoopDifficulty
{
    Relaxed = 0,
    Standard = 1,
    Hard = 2
}

internal readonly record struct CoopBalanceCurve(double MobHpPerPlayer, double BossHpPerPlayer, double MobDamagePerPlayer);

internal readonly record struct CoopBalanceValues(double MobHp, double BossHp, double MobDamage);

internal static class CoopBalance
{
    public const string Tag = "BALANCE";
    public const int MaxScaledPlayers = 4;
    public const double MinMultiplier = 0.25;
    public const double MaxMultiplier = 8.0;

    public static readonly CoopDifficulty[] All = [CoopDifficulty.Relaxed, CoopDifficulty.Standard, CoopDifficulty.Hard];

    public static CoopBalanceCurve Curve(CoopDifficulty difficulty) => difficulty switch
    {
        CoopDifficulty.Relaxed => new(0.40, 1.00, 0.00),
        CoopDifficulty.Hard => new(0.90, 2.00, 0.30),
        _ => new(0.60, 1.50, 0.15)
    };

    public static CoopBalanceValues For(CoopDifficulty difficulty, int players)
    {
        var curve = Curve(difficulty);
        var extra = Math.Clamp(players, 1, MaxScaledPlayers) - 1;
        return new(
            1 + curve.MobHpPerPlayer * extra,
            1 + curve.BossHpPerPlayer * extra,
            1 + curve.MobDamagePerPlayer * extra);
    }

    public static double Sanitize(double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0)
            return 1.0;
        return Math.Clamp(multiplier, MinMultiplier, MaxMultiplier);
    }

    public static int ScaleDamage(int damage, double multiplier)
    {
        if (damage <= 0)
            return damage;
        multiplier = Sanitize(multiplier);
        if (Math.Abs(multiplier - 1.0) < 0.0001)
            return damage;
        var scaled = Math.Round(damage * multiplier, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, 1, int.MaxValue);
    }

    public static string ToWire(CoopDifficulty difficulty) => difficulty switch
    {
        CoopDifficulty.Relaxed => "relaxed",
        CoopDifficulty.Hard => "hard",
        _ => "standard"
    };

    public static CoopDifficulty Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "relaxed" => CoopDifficulty.Relaxed,
        "hard" => CoopDifficulty.Hard,
        _ => CoopDifficulty.Standard
    };

    public static CoopDifficulty Next(CoopDifficulty difficulty) => difficulty switch
    {
        CoopDifficulty.Relaxed => CoopDifficulty.Standard,
        CoopDifficulty.Standard => CoopDifficulty.Hard,
        _ => CoopDifficulty.Relaxed
    };

    public static string BuildLine(CoopDifficulty difficulty, double mobDamage)
        => $"{Tag}|{ToWire(difficulty)}|{Sanitize(mobDamage).ToString("0.####", CultureInfo.InvariantCulture)}";

    public static bool TryParsePayload(string payload, out CoopDifficulty difficulty, out double mobDamage)
    {
        difficulty = CoopDifficulty.Standard;
        mobDamage = 1.0;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Trim().Split('|');
        if (parts.Length < 2 || parts[0] is not ("relaxed" or "standard" or "hard"))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value <= 0)
            return false;

        difficulty = Parse(parts[0]);
        mobDamage = Sanitize(value);
        return true;
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP BALANCE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Множитель = 1 + шаг × (игроков − 1), игроков считается не больше 4.
 *  Шаги (здоровье мобов / боссов / урон мобов по героям):
 *    Лёгкая 0.40 / 1.00 / 0.00 · Стандарт 0.60 / 1.50 / 0.15 · Хардкор 0.90 / 2.00 / 0.30.
 *  Вдвоём стандарт: мобы ×1.6 HP, боссы ×2.5 HP, урон ×1.15.
 *  Лут у каждого свой (2× урон отряда), агро делится, есть ревайв — отсюда HP < ×2 у мобов
 *  и прибавка к урону. Ручные множители из настроек умножаются поверх.
 *  Провод: BALANCE|<relaxed|standard|hard>|<итоговый множитель урона> (хост → клиенты).
 *  ScaleDamage: урон > 0 никогда не падает до 0, множитель зажат в 0.25..8.
 * ═══════════════════════════════════════
*/
