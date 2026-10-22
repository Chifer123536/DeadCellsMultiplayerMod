using System.Globalization;
using System.Reflection;
using DeadCellsMultiplayerMod.PortableCore;
using ModCore.Modules;
using Serilog;

namespace DeadCellsMultiplayerMod.I18n;

internal static class CoopText
{
    public const string Auto = MultiplayerSettingsStorage.ModLanguageAuto;
    public const string English = "en";
    public const string Russian = "ru";

    public static readonly string[] Choices = { Auto, English, Russian };

    static readonly string[] BundledLanguages = { "ru", "es", "zh" };
    static readonly Dictionary<string, PoCatalog> Catalogs = LoadCatalogs();
    static string? _mode;

    public static event Action? Changed;

    public static string Mode => _mode ??= MultiplayerSettingsStorage.ModLanguage;

    public static string GameLanguage
    {
        get
        {
            try
            {
                var lang = dc.Lang.Class.LANG?.ToString();
                return string.IsNullOrWhiteSpace(lang) ? English : lang.Trim().ToLowerInvariant();
            }
            catch
            {
                return English;
            }
        }
    }

    public static string EffectiveLanguage
    {
        get
        {
            var mode = Mode;
            if (mode != Auto)
                return mode;

            var game = GameLanguage;
            return game == English || Catalogs.ContainsKey(game) ? game : English;
        }
    }

    public static bool FollowsGame => EffectiveLanguage == GameLanguage;

    public static void SetMode(string mode)
    {
        MultiplayerSettingsStorage.ModLanguage = mode;
        var applied = MultiplayerSettingsStorage.ModLanguage;
        if (applied == _mode)
            return;

        _mode = applied;
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning("[CoopText] Language change handler failed: {Message}", ex.Message);
        }
    }

    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key ?? string.Empty;

        var lang = EffectiveLanguage;
        if (Catalogs.TryGetValue(lang, out var catalog) && catalog.TryGet(key, out var value))
            return value;

        if (lang == GameLanguage)
            return Game(key);

        return key;
    }

    public static string F(string format, params object[] args)
    {
        var localized = T(format);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, localized, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }

    public static string Game(string key)
    {
        try
        {
            return GetText.Instance.GetString(key) ?? key;
        }
        catch
        {
            return key;
        }
    }

    public static string ChoiceLabel(string mode) => mode switch
    {
        English => "English",
        Russian => "Русский",
        _ => T("As in game")
    };

    public static string DlcName(int bit, string englishName)
    {
        if (!FollowsGame)
            return englishName;

        var key = bit switch
        {
            CoopDlcMask.RiseOfTheGiant => "Rise of The Giant",
            CoopDlcMask.BadSeed => "The Bad Seed",
            CoopDlcMask.FatalFalls => "Fatal Falls",
            CoopDlcMask.TheQueenAndTheSea => "The Queen and the Sea",
            CoopDlcMask.ReturnToCastlevania => "DLCP_NAME_TEXT",
            _ => string.Empty
        };

        if (key.Length == 0)
            return englishName;

        var name = Game(key);
        return string.IsNullOrWhiteSpace(name) || name == key ? englishName : name;
    }

    public static void Fit(dc.ui.Text? text)
    {
        if (text == null || FollowsGame)
            return;

        try
        {
            text.autoCharset(null);
        }
        catch
        {
        }
    }

    public static void FitTree(dc.h2d.Object? root)
    {
        if (root == null || FollowsGame)
            return;

        try
        {
            if (root is dc.ui.Text text)
                text.autoCharset(null);

            var children = root.children;
            if (children == null)
                return;

            for (var i = 0; i < children.length; i++)
            {
                if (children.array[i] is dc.h2d.Object child)
                    FitTree(child);
            }
        }
        catch
        {
        }
    }

    static Dictionary<string, PoCatalog> LoadCatalogs()
    {
        var catalogs = new Dictionary<string, PoCatalog>(StringComparer.Ordinal);
        var assembly = typeof(CoopText).Assembly;
        foreach (var lang in BundledLanguages)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream($"CoopLang.main.{lang}.po");
                if (stream == null)
                    continue;

                using var reader = new StreamReader(stream);
                var catalog = PoCatalog.Parse(reader.ReadToEnd());
                if (catalog.Count > 0)
                    catalogs[lang] = catalog;
            }
            catch (Exception ex)
            {
                Log.Warning("[CoopText] Failed to load {Lang} catalog: {Message}", lang, ex.Message);
            }
        }

        return catalogs;
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP TEXT — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Язык мода: настройка ModLanguage = auto | en | ru. auto → язык игры, если для него есть каталог, иначе en.
 *  Каталоги — main.{ru,es,zh}.po, вшиты в dll (EmbeddedResource CoopLang.main.<lang>.po).
 *  T(key): каталог языка мода → (если язык мода = язык игры) словарь игры → сам ключ (английский).
 *  Game(key): строка из словаря игры (французские ключи игры, «Retour», «Jouer»…).
 *  Fit(text) / FitTree(obj): если язык мода ≠ язык игры, подбирает шрифт под символы строки
 *  (кириллица при EN-игре); FitTree обходит все Text внутри виджета.
 *  DlcName: при совпадении языков — локализованное имя DLC из игры, иначе английское.
 *  SetMode сохраняет выбор и шлёт Changed — открытые меню мода перестраиваются.
 * ═══════════════════════════════════════
*/
