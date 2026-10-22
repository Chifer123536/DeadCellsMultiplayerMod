using dc.pr;
using DeadCellsMultiplayerMod.I18n;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Serilog;

namespace DeadCellsMultiplayerMod
{
    internal static class CoopMenu
    {
        public const int AccentColor = 0xFFDE00;
        public const int InfoColor = 0xB9BDC7;
        public const int MutedColor = 0x7A817B;
        public const int WarningColor = 0xFF9E5E;
        public const int ErrorColor = 0xFF6B5E;
        public const int GoodColor = 0x8FD16A;

        const int GroupSpacing = 9;

        static TitleScreen? _screen;
        static Action? _back;
        static string _id = string.Empty;
        static int _restoreSelection;
        static int _items;

        public static string CurrentId => _id;

        public static bool IsOpen(TitleScreen screen, string id) =>
            ReferenceEquals(screen, _screen) && _id == id;

        public static void Begin(TitleScreen screen, string id, string title, Action? back)
        {
            var rebuild = ReferenceEquals(screen, _screen) && _id == id;
            _restoreSelection = rebuild ? ReadSelection(screen) : 0;
            _screen = screen;
            _id = id;
            _back = back;
            _items = 0;

            screen.isMainMenu = false;
            screen.clearMenu();
            HideNews(screen);

            if (!string.IsNullOrWhiteSpace(title))
                Header(title);
        }

        public static void Header(string text, int color = AccentColor)
        {
            var screen = _screen;
            if (screen == null)
                return;

            var line = dc.Assets.Class.makeMedievalText(text.AsHaxeString(), false, screen.menuItemsWrapper, null);
            line.textColor = color;
            CoopText.Fit(line);
        }

        public static void Info(string text, int color = InfoColor)
        {
            var screen = _screen;
            if (screen == null || string.IsNullOrWhiteSpace(text))
                return;

            var line = dc.Assets.Class.makeText(text.AsHaxeString(), color, false, screen.menuItemsWrapper);
            line.textColor = color;
            CoopText.Fit(line);
        }

        public static void Gap()
        {
            var screen = _screen;
            if (screen == null)
                return;

            try
            {
                var scale = screen.get_pixelScale.Invoke();
                screen.menuItemsWrapper.addSpacing((int)(scale * GroupSpacing));
            }
            catch
            {
            }
        }

        public static void Item(string label, Action onClick, string? help = null, bool enabled = true, int? color = null)
        {
            var screen = _screen;
            if (screen == null)
                return;

            int textColor = color ?? 0xFFFFFF;
            var cb = new HlAction(() =>
            {
                try
                {
                    onClick();
                }
                catch (Exception ex)
                {
                    Log.Warning("[CoopMenu] '{Label}' failed: {Message}", label, ex.Message);
                }
            });

            var helpText = string.IsNullOrWhiteSpace(help) ? null : help.AsHaxeString();
            var item = screen.addMenu(label.AsHaxeString(), cb, helpText, enabled, Ref<int>.From(ref textColor));
            CoopText.Fit(item.t);
            _items++;
        }

        public static void Commit()
        {
            var screen = _screen;
            if (screen == null)
                return;

            try
            {
                var label = screen.fControlLabel;
                if (label != null)
                {
                    label.set_visible(_back != null);
                    label.reflow();
                }
            }
            catch
            {
            }

            if (_items == 0)
                return;

            var index = Math.Clamp(_restoreSelection, 0, _items - 1);
            try
            {
                screen.select(index, Ref<bool>.Null);
            }
            catch (Exception ex)
            {
                Log.Debug("[CoopMenu] select failed: {Message}", ex.Message);
            }
        }

        public static bool TryHandleBack(TitleScreen screen)
        {
            if (_back == null || !ReferenceEquals(screen, _screen))
                return false;

            try
            {
                if (screen.fControlLabel == null || !screen.fControlLabel.visible)
                    return false;
            }
            catch
            {
                return false;
            }

            var back = _back;
            try
            {
                back();
            }
            catch (Exception ex)
            {
                Log.Warning("[CoopMenu] Back failed: {Message}", ex.Message);
            }
            return true;
        }

        public static void Close()
        {
            var screen = _screen;
            if (screen != null)
                ShowNews(screen);

            _screen = null;
            _back = null;
            _id = string.Empty;
            _restoreSelection = 0;
            _items = 0;
        }

        static int ReadSelection(TitleScreen screen)
        {
            try
            {
                return Math.Max(0, screen.curMenu);
            }
            catch
            {
                return 0;
            }
        }

        static void ShowNews(TitleScreen screen)
        {
            try
            {
                var news = screen.news;
                if (news == null)
                    return;
                news.hidden = false;
                news.updateVisible();
            }
            catch
            {
            }
        }

        static void HideNews(TitleScreen screen)
        {
            try
            {
                var news = screen.news;
                if (news == null)
                    return;
                news.hidden = true;
                news.updateVisible();
            }
            catch
            {
            }

            try
            {
                screen.leaderboard?.set_visible(false);
            }
            catch
            {
            }
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP MENU — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Экраны мультиплеера строятся родным меню титульника: TitleScreen.addMenu (шрифт, курсор,
 *  звуки, клавиатура/геймпад, подсказка снизу) — как playMenu самой игры.
 *  Begin(id, title, back): clearMenu, прячет новости/лидерборд, заголовок золотом в колонке меню.
 *  Info/Gap — невыбираемые строки и отступы внутри menuItemsWrapper (clearMenu их удаляет).
 *  Commit: подсказка «Назад» видна только если у экрана есть back; при перестройке того же экрана
 *  курсор остаётся на прежнем пункте.
 *  Esc/B: игра при видимой подсказке зовёт mainMenu() → хук → TryHandleBack → back() экрана.
 * ═══════════════════════════════════════
*/
