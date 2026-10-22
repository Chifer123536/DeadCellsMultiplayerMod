using dc.pr;
using ModCore.Modules;
using DeadCellsMultiplayerMod.PortableCore;

namespace DeadCellsMultiplayerMod
{
    internal static partial class LobbySession
    {
        internal static string GetSessionDlcLabel()
        {
            var mask = CoopDlcSession.SessionMask;
            if (mask < 0)
                return Localize("DLC: waiting for host");
            return Localize("DLC:") + " " + (mask == 0 ? Localize("none") : CoopDlcMask.Describe(mask));
        }

        internal static void ShowHostDlcMenu(TitleScreen screen)
        {
            if (_menuRebuildDepth > 0)
                return;
            _menuRebuildDepth++;
            var prevSuppress = _suppressAutoButton;
            _suppressAutoButton = true;
            var prevIsMain = GetIsMainMenu(screen);
            try
            {
                SetIsMainMenu(screen, false);
                screen.clearMenu();

                UiBegin();
                UiInfo(Localize("DLC for this co-op session. Only DLC owned by every player can be enabled."), 0x9098A8);
                if (_inActualRun)
                    UiInfo(Localize("A run is in progress: changes apply to the next run."), 0xFFC070);

                foreach (var choice in CoopDlcSession.DescribeHostChoices())
                {
                    var bit = choice.Dlc.Bit;
                    var mark = choice.Enabled ? "[x] " : "[  ] ";
                    if (choice.Available)
                    {
                        UiButton(
                            mark + choice.Dlc.Name,
                            () =>
                            {
                                CoopDlcSession.ToggleHostPreference(bit);
                                ShowHostDlcMenu(screen);
                            },
                            Localize("Toggle this DLC for the session"),
                            true,
                            choice.Enabled ? 0x59D5FF : 0xB0B0B0);
                    }
                    else
                    {
                        UiButton(
                            mark + choice.Dlc.Name,
                            () => { },
                            Localize("Missing for:") + " " + choice.MissingFor,
                            false);
                    }
                }

                UiInfo(GetSessionDlcLabel(), 0xE0E0E0);
                UiButton(GetText.Instance.GetString("Back"), () => ShowHostStatusMenu(screen), Localize("Back to lobby"));

                RemoveMenuItems(screen, "About Core Modding", GetText.Instance.GetString("Play multiplayer"));
                _inHostStatusMenu = false;
                _inClientWaitingMenu = false;
                UiCommit(showLobby: true);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to open DLC menu: {Message}", ex.Message);
            }
            finally
            {
                SetIsMainMenu(screen, prevIsMain);
                _suppressAutoButton = prevSuppress;
                _menuRebuildDepth--;
            }
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  LOBBY DLC MENU — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Хост: кнопка «DLC» в лобби → список пяти DLC. Доступные (есть у всех) переключаются галочкой,
 *  недоступные серые с подсказкой, у кого их нет. Переключение → CoopDlcSession пересчитывает и шлёт DLCSET.
 *  Строка «DLC: …» в лобби хоста и клиента показывает текущий набор сессии.
 * ═══════════════════════════════════════
*/
