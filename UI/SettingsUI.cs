using System.Globalization;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using DeadCellsMultiplayerMod.I18n;
using DeadCellsMultiplayerMod.PortableCore;
using dc.ui;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Utilities;
using static DeadCellsMultiplayerMod.I18n.CoopText;

namespace DeadCellsMultiplayerMod.UI;

public class SettingsUI :
    IEventReceiver,
    IOnAdvancedModuleInitializing
{
    static readonly double[] HpPresets = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };
    static readonly double[] DamagePresets = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };
    static readonly string[] DebugPerkFallbackChoices =
    {
        "P_Yolo",
        "P_DeadInside",
        "P_Necromancy",
        "P_Recovery",
        "P_Vengeance"
    };

    static bool _hooksAttached;
    static bool _isMultiplayerSettingsOpen;
    static int _multiplayerSettingsOptionsId = -1;

    ModEntry mod { get; set; }

    public SettingsUI(ModEntry entry)
    {
        mod = entry;
        EventSystem.AddReceiver(this);
    }

    void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
    {
        entry.Logger.Information("\x1b[32m[[ModEntry.SettingsUI] Initializing SettingsUI...]\x1b[0m ");

        if (_hooksAttached)
            return;

        Hook_Options.showMain += Hook_Options_showMain;
        Hook_Options.showCredits += Hook_Options_showCredits;
        Hook_Options.onDispose += Hook_Options_onDispose;
        _hooksAttached = true;
    }

    void Hook_Options_showMain(Hook_Options.orig_showMain orig, Options self)
    {
        if (self == null)
        {
            orig(self);
            return;
        }

        if (IsMultiplayerSettingsContext(self))
        {
            MultiplayerSettingsStorage.Save();
            ResetMenuState();
        }

        try
        {
            int leftPadding = 5;
            var entry = self.addSimpleWidget(
                LobbySession.Localize("Multiplayer settings").AsHaxeString(),
                null,
                new HlAction(() => OpenMultiplayerSettingsMenu(self)),
                Ref<int>.From(ref leftPadding),
                null);
            CoopText.FitTree(entry);
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to add Multiplayer settings button");
        }

        orig(self);
    }

    void Hook_Options_showCredits(Hook_Options.orig_showCredits orig, Options self)
    {
        if (!IsMultiplayerSettingsContext(self))
        {
            orig(self);
            return;
        }

        BuildMultiplayerSettingsSection(self);
    }

    void Hook_Options_onDispose(Hook_Options.orig_onDispose orig, Options self)
    {
        var wasMultiplayerSettings = IsMultiplayerSettingsContext(self);
        orig(self);

        if (wasMultiplayerSettings)
        {
            MultiplayerSettingsStorage.Save();
            ResetMenuState();
        }
    }

    void OpenMultiplayerSettingsMenu(Options self)
    {
        try
        {
            if (self == null || self.destroyed)
                return;

            _isMultiplayerSettingsOpen = true;
            _multiplayerSettingsOptionsId = self.uniqId;
            self.setSection(new OptionsSection.S_Credits());
        }
        catch (Exception ex)
        {
            ResetMenuState();
            mod.Logger.Warning(ex, "[NetMod] Failed to open multiplayer settings menu");
        }
    }

    static bool IsMultiplayerSettingsContext(Options self)
    {
        return _isMultiplayerSettingsOpen
            && self != null
            && !self.destroyed
            && self.uniqId == _multiplayerSettingsOptionsId;
    }

    static void ResetMenuState()
    {
        _isMultiplayerSettingsOpen = false;
        _multiplayerSettingsOptionsId = -1;
    }

    void BuildMultiplayerSettingsSection(Options self)
    {
        try
        {
            if (!IsMultiplayerSettingsContext(self))
                return;

            self.title?.set_text(LobbySession.Localize("Multiplayer settings").AsHaxeString());
            self.createScroller(0.0);

            var parent = self.scrollerFlow;
            if (parent == null)
                return;

            AddGeneralSection(self, parent);
            AddHostSection(self, parent);
            AddAchievementsSection(self, parent);
            AddControlsSection(self, parent);
            if (MultiplayerSettingsStorage.IsDebugSectionEnabled)
                AddDebugSection(self, parent);

            int leftPadding = 5;
            self.addSimpleWidget(
                LobbySession.Localize("Back").AsHaxeString(),
                null,
                new HlAction(() =>
                {
                    if (self != null && !self.destroyed)
                        self.onQuit();
                }),
                Ref<int>.From(ref leftPadding),
                parent);

            CoopText.FitTree(parent);
            CoopText.FitTree(self.title);
            self.updateScroller();
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to build multiplayer settings menu");
        }
    }

    static void AddGeneralSection(Options self, dc.h2d.Flow parent)
    {
        AddSection(self, parent, T("General"));

        var choices = CoopText.Choices;
        var labels = new string[choices.Length];
        var current = 0;
        for (var i = 0; i < choices.Length; i++)
        {
            labels[i] = CoopText.ChoiceLabel(choices[i]);
            if (choices[i] == CoopText.Mode)
                current = i;
        }

        AddList(self, parent,
            T("Mod language"),
            T("Language of the co-op menus. \"As in game\" follows the game language."),
            labels, current, index => OnModLanguageChanged(self, index));
    }

    static void AddHostSection(Options self, dc.h2d.Flow parent)
    {
        AddSection(self, parent, T("Host settings"));

        var difficulties = CoopBalance.All;
        var difficultyLabels = new string[difficulties.Length];
        var currentDifficulty = 0;
        for (var i = 0; i < difficulties.Length; i++)
        {
            difficultyLabels[i] = LobbySession.GetDifficultyName(difficulties[i]);
            if (difficulties[i] == CoopBalanceSync.HostDifficulty)
                currentDifficulty = i;
        }

        AddList(self, parent,
            T("Co-op difficulty"),
            T("Scales mob health, boss health and mob damage by the number of players. Set by the host."),
            difficultyLabels, currentDifficulty, OnCoopDifficultyChanged);

        AddPercentList(self, parent, T("Mob health"), HpPresets, MultiplayerSettingsStorage.MobsHpMultiplier, index =>
        {
            MultiplayerSettingsStorage.MobsHpMultiplier = HpPresets[index];
            ModEntry._net?.SendHpMultipliers();
        });

        AddPercentList(self, parent, T("Boss health"), HpPresets, MultiplayerSettingsStorage.BossesHpMultiplier, index =>
        {
            MultiplayerSettingsStorage.BossesHpMultiplier = HpPresets[index];
            ModEntry._net?.SendHpMultipliers();
        });

        AddPercentList(self, parent, T("Mob damage"), DamagePresets, MultiplayerSettingsStorage.MobsDamageMultiplier, index =>
        {
            MultiplayerSettingsStorage.MobsDamageMultiplier = DamagePresets[index];
            CoopBalanceSync.OnSettingsChanged();
        });

        var modes = ScrollChoiceWire.AllModes;
        var modeLabels = new string[modes.Length];
        var currentMode = 0;
        for (var i = 0; i < modes.Length; i++)
        {
            modeLabels[i] = LobbySession.Localize(ScrollChoiceModeLabel(modes[i]));
            if (modes[i] == ScrollChoiceSync.HostMode)
                currentMode = i;
        }

        AddList(self, parent,
            T("Scroll pick"),
            T("What happens for everyone while a player picks a scroll. Set by the host."),
            modeLabels, currentMode, index =>
            {
                if (index >= 0 && index < modes.Length)
                    ScrollChoiceSync.SetHostMode(modes[index]);
            });
    }

    static void AddAchievementsSection(Options self, dc.h2d.Flow parent)
    {
        AddSection(self, parent, T("Achievements"));

        AddToggle(self, parent,
            T("Steam achievements"),
            T("Unlock Steam achievements while playing with the mod. Off until the game restarts once debug cheats were used."),
            MultiplayerSettingsStorage.SteamAchievements,
            () => MultiplayerSettingsStorage.SteamAchievements = !MultiplayerSettingsStorage.SteamAchievements);

        AddToggle(self, parent,
            T("Shared achievements"),
            T("Also unlock the achievements your teammates get. They need the same mod version."),
            MultiplayerSettingsStorage.ShareAchievements,
            () => MultiplayerSettingsStorage.ShareAchievements = !MultiplayerSettingsStorage.ShareAchievements);
    }

    static void AddControlsSection(Options self, dc.h2d.Flow parent)
    {
        AddSection(self, parent, T("Controls"));

        int leftPadding = 5;
        self.addSimpleWidget(
            LobbySession.Localize("Emote wheel: hold T or L3").AsHaxeString(),
            LobbySession.Localize("Press the left stick without tilting it, pick with the right stick or the mouse, then release.").AsHaxeString(),
            new HlAction(() => { }),
            Ref<int>.From(ref leftPadding),
            parent);
    }

    static void AddDebugSection(Options self, dc.h2d.Flow parent)
    {
        AddSection(self, parent, T("Debug"));

        AddToggle(self, parent, T("Player immortal"), null,
            MultiplayerSettingsStorage.DebugPlayerImmortal,
            () => MultiplayerSettingsStorage.DebugPlayerImmortal = !MultiplayerSettingsStorage.DebugPlayerImmortal);

        AddToggle(self, parent, T("Use Explorer's Rune"), null,
            MultiplayerSettingsStorage.DebugUseExplorersRune,
            () => MultiplayerSettingsStorage.DebugUseExplorersRune = !MultiplayerSettingsStorage.DebugUseExplorersRune);

        var perks = BuildDebugPerkChoices();
        AddList(self, parent, T("Start perk"), null, perks.ToArray(), ResolveCurrentDebugPerkIndex(perks), index =>
        {
            if (index >= 0 && index < perks.Count)
                MultiplayerSettingsStorage.DebugStartPerkId = perks[index];
        });

        AddToggle(self, parent, T("Enable mobs sync"), null,
            MultiplayerSettingsStorage.EnableMobsSync,
            ToggleMobsSync);

        AddToggle(self, parent, T("Sync vertical position"),
            T("Applies host vertical position to gravity mobs. Off: X-only for walkers; flying mobs still sync Y. On: can reduce desync but may snap ground mobs."),
            MultiplayerSettingsStorage.SyncVerticalPosition,
            () => MultiplayerSettingsStorage.SyncVerticalPosition = !MultiplayerSettingsStorage.SyncVerticalPosition);

        AddToggle(self, parent, T("Mobs sync trace logging"), null,
            MultiplayerSettingsStorage.DebugMobsSyncTrace,
            () => MultiplayerSettingsStorage.DebugMobsSyncTrace = !MultiplayerSettingsStorage.DebugMobsSyncTrace);

        AddToggle(self, parent, T("Boss sync trace logging"), null,
            MultiplayerSettingsStorage.DebugBossSyncTrace,
            () => MultiplayerSettingsStorage.DebugBossSyncTrace = !MultiplayerSettingsStorage.DebugBossSyncTrace);

        AddToggle(self, parent, T("Preview party HUD"), null,
            MultiplayerSettingsStorage.DebugPartyHudPreview,
            () => MultiplayerSettingsStorage.DebugPartyHudPreview = !MultiplayerSettingsStorage.DebugPartyHudPreview);

        AddToggle(self, parent, T("Show perf logs"),
            T("Controls threshold-based [Perf] hitch and slowdown logging."),
            MultiplayerSettingsStorage.ShowPerfLogs,
            () => MultiplayerSettingsStorage.ShowPerfLogs = !MultiplayerSettingsStorage.ShowPerfLogs);
    }

    static void AddSection(Options self, dc.h2d.Flow parent, string label)
    {
        var localized = label.AsHaxeString();
        try
        {
            self.addSeparator(localized, parent);
        }
        catch
        {
            int leftPadding = 0;
            self.addSimpleWidget(localized, null, new HlAction(() => { }), Ref<int>.From(ref leftPadding), parent);
        }
    }

    static void AddList(Options self, dc.h2d.Flow parent, string label, string? description, string[] values, int current, Action<int> onChange)
    {
        var labels = ArrayUtils.CreateDyn();
        foreach (var value in values)
            labels.array.pushDyn(value.AsHaxeString());

        int padding = 0;
        self.addListWidget(
            label.AsHaxeString(),
            description?.AsHaxeString(),
            new HlAction<int>(index => onChange(index)),
            Math.Clamp(current, 0, Math.Max(0, values.Length - 1)),
            values.Length,
            (dc.hl.types.ArrayObj)labels.array,
            Ref<int>.From(ref padding),
            parent);
    }

    static void AddPercentList(Options self, dc.h2d.Flow parent, string label, double[] presets, double current, Action<int> onChange)
    {
        var labels = new string[presets.Length];
        for (var i = 0; i < presets.Length; i++)
            labels[i] = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(presets[i] * 100.0):0}%");

        AddList(self, parent, label,
            T("Extra multiplier on top of co-op difficulty. Set by the host."),
            labels, NearestPreset(presets, current), index =>
            {
                if (index >= 0 && index < presets.Length)
                    onChange(index);
            });
    }

    static void AddToggle(Options self, dc.h2d.Flow parent, string label, string? description, bool current, Func<bool> toggle)
    {
        var value = current;
        self.addToggleWidget(
            label.AsHaxeString(),
            description?.AsHaxeString(),
            new HlFunc<bool>(toggle),
            Ref<bool>.From(ref value),
            parent);
    }

    static int NearestPreset(double[] presets, double value)
    {
        var best = 0;
        for (var i = 1; i < presets.Length; i++)
        {
            if (Math.Abs(presets[i] - value) < Math.Abs(presets[best] - value))
                best = i;
        }
        return best;
    }

    static string ScrollChoiceModeLabel(ScrollChoiceMode mode) => mode switch
    {
        ScrollChoiceMode.Off => "No pause",
        ScrollChoiceMode.Pause => "Pause",
        _ => "Pause and show choice"
    };

    static void OnModLanguageChanged(Options self, int index)
    {
        var choices = CoopText.Choices;
        if (index < 0 || index >= choices.Length || choices[index] == CoopText.Mode)
            return;

        CoopText.SetMode(choices[index]);
        MainThreadPump.EnqueueMainThread(() =>
        {
            try
            {
                if (IsMultiplayerSettingsContext(self))
                    self.setSection(new OptionsSection.S_Credits());
            }
            catch
            {
            }
        });
    }

    static void OnCoopDifficultyChanged(int index)
    {
        var difficulties = CoopBalance.All;
        if (index < 0 || index >= difficulties.Length)
            return;
        MultiplayerSettingsStorage.CoopDifficulty = CoopBalance.ToWire(difficulties[index]);
        CoopBalanceSync.OnSettingsChanged();
    }

    static bool ToggleMobsSync()
    {
        var enabled = !MultiplayerSettingsStorage.EnableMobsSync;
        MultiplayerSettingsStorage.EnableMobsSync = enabled;

        if (!enabled)
        {
            MobsSynchronization.ClearTrackingForLevelChange();
            try { LobbySession.NetRef?.ClearMobSyncQueues(); } catch { }
        }

        return enabled;
    }

    static List<string> BuildDebugPerkChoices()
    {
        var list = new List<string> { MultiplayerSettingsStorage.NoStartPerkValue };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MultiplayerSettingsStorage.NoStartPerkValue
        };

        try
        {
            var byIndex = dc.Data.Class.item?.byIndex;
            if (byIndex != null)
            {
                var len = byIndex.get_length();
                for (var i = 0; i < len; i++)
                {
                    if (byIndex.getDyn(i) is not HaxeDynObj ho)
                        continue;

                    string? id = null;
                    try
                    {
                        id = ho.ToVirtual<virtual_ambiantDesc_castCD_cellCost_commonProps_dlc_droppable_gameplayDesc_group_icon_id_legendAffixes_moneyCost_name_props_synergy_tags_tier1_tier2_>()?.id?.ToString();
                    }
                    catch
                    {
                    }
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    var trimmed = id.Trim();
                    if (trimmed.StartsWith("P_", StringComparison.OrdinalIgnoreCase) && seen.Add(trimmed))
                        list.Add(trimmed);
                }
            }
        }
        catch
        {
        }

        if (list.Count == 1)
        {
            foreach (var fallback in DebugPerkFallbackChoices)
            {
                if (seen.Add(fallback))
                    list.Add(fallback);
            }
        }

        if (list.Count > 2)
            list.Sort(1, list.Count - 1, StringComparer.OrdinalIgnoreCase);

        return list;
    }

    static int ResolveCurrentDebugPerkIndex(List<string> perkChoices)
    {
        var selected = MultiplayerSettingsStorage.DebugStartPerkId;
        for (var i = 0; i < perkChoices.Count; i++)
        {
            if (string.Equals(perkChoices[i], selected, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
}

/*
 * ═══════════════════════════════════════
 *  SETTINGS UI — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Кнопка «Настройки мультиплеера» в главном экране опций игры; страница рисуется вместо
 *  раздела Credits (S_Credits) родными виджетами Options: addSeparator / addListWidget /
 *  addToggleWidget / addSimpleWidget. Сохранение при выходе со страницы и при закрытии опций.
 *  Разделы: Общие (язык) · Настройки хоста (сложность, % здоровья и урона, выбор свитка) ·
 *  Достижения (Steam, общие) · Управление (подсказка колеса) · Debug (только при запуске из исходников).
 *  Множители — списки готовых процентов: родной слайдер печатает float после ×100 с хвостами.
 *  Сохранённое значение вне списка показывается ближайшим и не перезаписывается, пока его не сменят.
 * ═══════════════════════════════════════
*/
