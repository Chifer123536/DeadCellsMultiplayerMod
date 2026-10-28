using System;
using System.IO;
using ModCore.Storage;

namespace DeadCellsMultiplayerMod;

public enum DebugModuleId
{
    MultiplayerModLang,
    CineHooks,
    MultiplayerUI,
    LevelInit,
    MobsSynchronization,
    MinimapReveal,
    LevelExitSync,
    InteractionSync,
    ConnectionUI
}

public sealed class MultiplayerSettingsData
{
    public bool EnableMobsSync { get; set; } = true;

    public double MobsInterpolationQuality { get; set; } = 0.20;

    public double MobsHpMultiplier { get; set; } = 1.0;

    public double BossesHpMultiplier { get; set; } = 1.0;

    public double MobsDamageMultiplier { get; set; } = 1.0;

    public string CoopDifficulty { get; set; } = "standard";

    public string ScrollChoiceMode { get; set; } = "mirror";

    public bool SyncVerticalPosition { get; set; } = false;

    public bool SteamAchievements { get; set; } = true;

    public bool ShareAchievements { get; set; } = true;

    public int CoopDlcPreferenceMask { get; set; } = 0x1F;

    public string ModLanguage { get; set; } = MultiplayerSettingsStorage.ModLanguageAuto;

    public bool ModuleMultiplayerModLangEnabled { get; set; } = true;

    public bool ModuleCineHooksEnabled { get; set; } = true;

    public bool ModuleMultiplayerUIEnabled { get; set; } = true;

    public bool ModuleLevelInitEnabled { get; set; } = true;

    public bool ModuleMobsSynchronizationEnabled { get; set; } = true;

    public bool ModuleMinimapRevealEnabled { get; set; } = true;

    public bool ModuleLevelExitSyncEnabled { get; set; } = true;

    public bool ModuleInteractionSyncEnabled { get; set; } = true;

    public bool ModuleConnectionUIEnabled { get; set; } = true;

    public bool DebugPlayerImmortal { get; set; } = false;

    public bool DebugUseExplorersRune { get; set; } = false;

    public bool DebugMobsSyncTrace { get; set; } = false;

    public bool DebugBossSyncTrace { get; set; } = false;

    public bool DebugPartyHudPreview { get; set; } = false;

    public bool ShowPerfLogs { get; set; } = false;

    public string DebugStartPerkId { get; set; } = MultiplayerSettingsStorage.NoStartPerkValue;
}

public static class MultiplayerSettingsStorage
{
    private const string ConfigName = "DeadCellsMultiplayerMod.MultiplayerSettings";
    private const double InterpolationMin = 0.20;
    private const double InterpolationMax = 1.00;
    private const double HpMultiplierMin = 0.25;
    private const double HpMultiplierMax = 8.00;
    public const string NoStartPerkValue = "None";
    public const string ModLanguageAuto = "auto";

    private static readonly object SyncRoot = new();
    private static readonly Config<MultiplayerSettingsData> Config = new(ConfigName);
    private static bool? _isDebugSectionEnabledCache;

    public static bool IsDebugSectionEnabled
    {
        get
        {
            lock (SyncRoot)
            {
                _isDebugSectionEnabledCache ??= ResolveDebugSectionEnabledUnsafe();
                return _isDebugSectionEnabledCache.Value;
            }
        }
    }

    public static bool EnableMobsSync
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().EnableMobsSync;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.EnableMobsSync == value)
                    return;

                data.EnableMobsSync = value;
                SaveUnsafe();
            }
        }
    }

    public static double MobsInterpolationQuality
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().MobsInterpolationQuality;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, InterpolationMin, InterpolationMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.MobsInterpolationQuality, clamped))
                    return;

                data.MobsInterpolationQuality = clamped;
                SaveUnsafe();
            }
        }
    }

    public static double MobsHpMultiplier
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().MobsHpMultiplier;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, HpMultiplierMin, HpMultiplierMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.MobsHpMultiplier, clamped))
                    return;

                data.MobsHpMultiplier = clamped;
                SaveUnsafe();
            }
        }
    }

    public static double BossesHpMultiplier
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().BossesHpMultiplier;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, HpMultiplierMin, HpMultiplierMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.BossesHpMultiplier, clamped))
                    return;

                data.BossesHpMultiplier = clamped;
                SaveUnsafe();
            }
        }
    }

    public static double MobsDamageMultiplier
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().MobsDamageMultiplier;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, HpMultiplierMin, HpMultiplierMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.MobsDamageMultiplier, clamped))
                    return;

                data.MobsDamageMultiplier = clamped;
                SaveUnsafe();
            }
        }
    }

    public static string CoopDifficulty
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().CoopDifficulty;
        }
        set
        {
            lock (SyncRoot)
            {
                var normalized = NormalizeCoopDifficulty(value);
                var data = EnsureDataNormalizedUnsafe();
                if (string.Equals(data.CoopDifficulty, normalized, StringComparison.Ordinal))
                    return;

                data.CoopDifficulty = normalized;
                SaveUnsafe();
            }
        }
    }

    public static string ScrollChoiceMode
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().ScrollChoiceMode;
        }
        set
        {
            lock (SyncRoot)
            {
                var normalized = NormalizeScrollChoiceMode(value);
                var data = EnsureDataNormalizedUnsafe();
                if (string.Equals(data.ScrollChoiceMode, normalized, StringComparison.Ordinal))
                    return;

                data.ScrollChoiceMode = normalized;
                SaveUnsafe();
            }
        }
    }

    public static bool SteamAchievements
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().SteamAchievements;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.SteamAchievements == value)
                    return;

                data.SteamAchievements = value;
                SaveUnsafe();
            }
        }
    }

    public static bool ShareAchievements
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().ShareAchievements;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.ShareAchievements == value)
                    return;

                data.ShareAchievements = value;
                SaveUnsafe();
            }
        }
    }

    public static bool SyncVerticalPosition
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().SyncVerticalPosition;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.SyncVerticalPosition == value)
                    return;

                data.SyncVerticalPosition = value;
                SaveUnsafe();
            }
        }
    }

    public static int CoopDlcPreferenceMask
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().CoopDlcPreferenceMask & 0x1F;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                var normalized = value & 0x1F;
                if (data.CoopDlcPreferenceMask == normalized)
                    return;

                data.CoopDlcPreferenceMask = normalized;
                SaveUnsafe();
            }
        }
    }

    public static string ModLanguage
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().ModLanguage;
        }
        set
        {
            lock (SyncRoot)
            {
                var normalized = NormalizeModLanguage(value);
                var data = EnsureDataNormalizedUnsafe();
                if (string.Equals(data.ModLanguage, normalized, StringComparison.Ordinal))
                    return;

                data.ModLanguage = normalized;
                SaveUnsafe();
            }
        }
    }

    public static bool DebugPlayerImmortal
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().DebugPlayerImmortal;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.DebugPlayerImmortal == value)
                    return;

                data.DebugPlayerImmortal = value;
                SaveUnsafe();
            }
        }
    }

    public static bool DebugUseExplorersRune
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().DebugUseExplorersRune;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.DebugUseExplorersRune == value)
                    return;

                data.DebugUseExplorersRune = value;
                SaveUnsafe();
            }
        }
    }

    public static bool DebugMobsSyncTrace
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().DebugMobsSyncTrace;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.DebugMobsSyncTrace == value)
                    return;

                data.DebugMobsSyncTrace = value;
                SaveUnsafe();
            }
        }
    }

    public static bool DebugBossSyncTrace
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().DebugBossSyncTrace;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.DebugBossSyncTrace == value)
                    return;

                data.DebugBossSyncTrace = value;
                SaveUnsafe();
            }
        }
    }

    public static bool DebugPartyHudPreview
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().DebugPartyHudPreview;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.DebugPartyHudPreview == value)
                    return;

                data.DebugPartyHudPreview = value;
                SaveUnsafe();
            }
        }
    }

    public static bool ShowPerfLogs
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().ShowPerfLogs;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.ShowPerfLogs == value)
                    return;

                data.ShowPerfLogs = value;
                SaveUnsafe();
            }
        }
    }

    public static string DebugStartPerkId
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().DebugStartPerkId;
        }
        set
        {
            lock (SyncRoot)
            {
                var normalized = NormalizePerkId(value);
                var data = EnsureDataNormalizedUnsafe();
                if (string.Equals(data.DebugStartPerkId, normalized, StringComparison.Ordinal))
                    return;

                data.DebugStartPerkId = normalized;
                SaveUnsafe();
            }
        }
    }

    public static bool IsModuleEnabled(DebugModuleId moduleId)
    {
        lock (SyncRoot)
        {
            var data = EnsureDataNormalizedUnsafe();
            return moduleId switch
            {
                DebugModuleId.MultiplayerModLang => data.ModuleMultiplayerModLangEnabled,
                DebugModuleId.CineHooks => data.ModuleCineHooksEnabled,
                DebugModuleId.MultiplayerUI => data.ModuleMultiplayerUIEnabled,
                DebugModuleId.LevelInit => data.ModuleLevelInitEnabled,
                DebugModuleId.MobsSynchronization => data.ModuleMobsSynchronizationEnabled,
                DebugModuleId.MinimapReveal => data.ModuleMinimapRevealEnabled,
                DebugModuleId.LevelExitSync => data.ModuleLevelExitSyncEnabled,
                DebugModuleId.InteractionSync => data.ModuleInteractionSyncEnabled,
                DebugModuleId.ConnectionUI => data.ModuleConnectionUIEnabled,
                _ => true
            };
        }
    }

    public static void SetModuleEnabled(DebugModuleId moduleId, bool enabled)
    {
        lock (SyncRoot)
        {
            var data = EnsureDataNormalizedUnsafe();
            var changed = false;
            switch (moduleId)
            {
                case DebugModuleId.MultiplayerModLang:
                    changed = data.ModuleMultiplayerModLangEnabled != enabled;
                    data.ModuleMultiplayerModLangEnabled = enabled;
                    break;
                case DebugModuleId.CineHooks:
                    changed = data.ModuleCineHooksEnabled != enabled;
                    data.ModuleCineHooksEnabled = enabled;
                    break;
                case DebugModuleId.MultiplayerUI:
                    changed = data.ModuleMultiplayerUIEnabled != enabled;
                    data.ModuleMultiplayerUIEnabled = enabled;
                    break;
                case DebugModuleId.LevelInit:
                    changed = data.ModuleLevelInitEnabled != enabled;
                    data.ModuleLevelInitEnabled = enabled;
                    break;
                case DebugModuleId.MobsSynchronization:
                    changed = data.ModuleMobsSynchronizationEnabled != enabled;
                    data.ModuleMobsSynchronizationEnabled = enabled;
                    break;
                case DebugModuleId.MinimapReveal:
                    changed = data.ModuleMinimapRevealEnabled != enabled;
                    data.ModuleMinimapRevealEnabled = enabled;
                    break;
                case DebugModuleId.LevelExitSync:
                    changed = data.ModuleLevelExitSyncEnabled != enabled;
                    data.ModuleLevelExitSyncEnabled = enabled;
                    break;
                case DebugModuleId.InteractionSync:
                    changed = data.ModuleInteractionSyncEnabled != enabled;
                    data.ModuleInteractionSyncEnabled = enabled;
                    break;
                case DebugModuleId.ConnectionUI:
                    changed = data.ModuleConnectionUIEnabled != enabled;
                    data.ModuleConnectionUIEnabled = enabled;
                    break;
            }

            if (changed)
                SaveUnsafe();
        }
    }

    public static void Save()
    {
        lock (SyncRoot)
            SaveUnsafe();
    }

    private static MultiplayerSettingsData EnsureDataNormalizedUnsafe()
    {
        var data = Config.Value ?? new MultiplayerSettingsData();
        bool changed = false;

        var interpolation = Clamp(data.MobsInterpolationQuality, InterpolationMin, InterpolationMax);
        if (!Approximately(data.MobsInterpolationQuality, interpolation))
        {
            data.MobsInterpolationQuality = interpolation;
            changed = true;
        }

        var mobsHp = Clamp(data.MobsHpMultiplier, HpMultiplierMin, HpMultiplierMax);
        if (!Approximately(data.MobsHpMultiplier, mobsHp))
        {
            data.MobsHpMultiplier = mobsHp;
            changed = true;
        }

        var bossesHp = Clamp(data.BossesHpMultiplier, HpMultiplierMin, HpMultiplierMax);
        if (!Approximately(data.BossesHpMultiplier, bossesHp))
        {
            data.BossesHpMultiplier = bossesHp;
            changed = true;
        }

        var mobsDamage = Clamp(data.MobsDamageMultiplier, HpMultiplierMin, HpMultiplierMax);
        if (!Approximately(data.MobsDamageMultiplier, mobsDamage))
        {
            data.MobsDamageMultiplier = mobsDamage;
            changed = true;
        }

        var normalizedDifficulty = NormalizeCoopDifficulty(data.CoopDifficulty);
        if (!string.Equals(data.CoopDifficulty, normalizedDifficulty, StringComparison.Ordinal))
        {
            data.CoopDifficulty = normalizedDifficulty;
            changed = true;
        }

        var normalizedScrollMode = NormalizeScrollChoiceMode(data.ScrollChoiceMode);
        if (!string.Equals(data.ScrollChoiceMode, normalizedScrollMode, StringComparison.Ordinal))
        {
            data.ScrollChoiceMode = normalizedScrollMode;
            changed = true;
        }

        var normalizedPerkId = NormalizePerkId(data.DebugStartPerkId);
        if (!string.Equals(data.DebugStartPerkId, normalizedPerkId, StringComparison.Ordinal))
        {
            data.DebugStartPerkId = normalizedPerkId;
            changed = true;
        }

        var normalizedLanguage = NormalizeModLanguage(data.ModLanguage);
        if (!string.Equals(data.ModLanguage, normalizedLanguage, StringComparison.Ordinal))
        {
            data.ModLanguage = normalizedLanguage;
            changed = true;
        }

        if (!ReferenceEquals(Config.Value, data))
        {
            Config.Value = data;
            changed = true;
        }

        if (changed)
            SaveUnsafe();

        return data;
    }

    private static bool ResolveDebugSectionEnabledUnsafe()
    {
#if DEBUG
        return true;
#else
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; depth < 8 && dir != null; depth++)
            {
                var csprojPath = Path.Combine(dir.FullName, "DeadCellsMultiplayerMod.csproj");
                if (File.Exists(csprojPath))
                {
                    var content = File.ReadAllText(csprojPath);
                    return content.IndexOf("<Debug>true</Debug>", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
        }

        return false;
#endif
    }

    private static string NormalizePerkId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return NoStartPerkValue;

        var trimmed = value.Trim();
        return string.Equals(trimmed, NoStartPerkValue, StringComparison.OrdinalIgnoreCase)
            ? NoStartPerkValue
            : trimmed;
    }

    private static string NormalizeModLanguage(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is "en" or "ru" ? trimmed : ModLanguageAuto;
    }

    private static string NormalizeCoopDifficulty(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is "relaxed" or "hard" ? trimmed : "standard";
    }

    static string NormalizeScrollChoiceMode(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is "off" or "pause" ? trimmed : "mirror";
    }

    private static void SaveUnsafe()
    {
        Config.Save();
    }

    private static bool Approximately(double left, double right)
    {
        return Math.Abs(left - right) <= 0.0001;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return min;
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
