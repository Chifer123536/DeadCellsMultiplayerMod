using System.Globalization;
using dc.pr;
using DeadCellsMultiplayerMod.I18n;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using DeadCellsMultiplayerMod.PortableCore;

namespace DeadCellsMultiplayerMod
{
    internal static partial class LobbySession
    {
        const string MenuHub = "coop.hub";
        const string MenuJoin = "coop.join";
        const string MenuOther = "coop.other";
        const string MenuLan = "coop.lan";
        const string MenuHostLobby = "coop.lobby.host";
        const string MenuClientLobby = "coop.lobby.client";
        const string MenuDlc = "coop.dlc";
        const string MenuMessage = "coop.message";

        internal readonly record struct LobbyPanelLine(string Text, int Color, bool IsCode = false, string Value = "");

        static void BuildCoopScreen(TitleScreen screen, string id, string title, Action? back, bool lobby, Action build)
        {
            var prevSuppress = _suppressAutoButton;
            _suppressAutoButton = true;
            try
            {
                StoreTitleScreen(screen);
                CoopMenu.Begin(screen, id, title, back);
                build();
                CoopMenu.Commit();
                ConnectionUI.ShowLobby(lobby);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to open menu {Menu}: {Message}", id, ex.Message);
            }
            finally
            {
                _suppressAutoButton = prevSuppress;
            }
        }

        internal static void ShowMultiplayerMenu(TitleScreen screen)
        {
            _steamFriendJoinPageActive = false;
            _inHostStatusMenu = false;
            _inClientWaitingMenu = false;

            BuildCoopScreen(screen, MenuHub, CoopText.T("Co-op"), () => ReturnFromMultiplayerHubToTitle(screen), lobby: false, () =>
            {
                CoopMenu.Info(CoopText.T("Play together with your Steam friends."));
                CoopMenu.Gap();
                CoopMenu.Item(
                    CoopText.T("Create lobby"),
                    () => StartSteamHost(screen, SteamConnect.SteamLobbyVisibility.FriendsOnly),
                    CoopText.T("Friends-only Steam lobby. Invite a friend once it is created."),
                    color: CoopMenu.AccentColor);
                CoopMenu.Item(
                    CoopText.T("Join a friend"),
                    () => ShowJoinTransportMenu(screen),
                    CoopText.T("Lobbies of Steam friends who are playing right now."));
                CoopMenu.Item(
                    CoopText.T("Other ways to connect"),
                    () => ShowOtherWaysMenu(screen),
                    CoopText.T("Public lobby, lobby code or local network."));
                CoopMenu.Gap();
                CoopMenu.Item(CoopText.T("Back"), () => ReturnFromMultiplayerHubToTitle(screen));
            });
        }

        internal static void ShowHostTransportMenu(TitleScreen screen) => ShowMultiplayerMenu(screen);

        internal static void ShowSteamHostModeMenu(TitleScreen screen) => ShowOtherWaysMenu(screen);

        internal static void ShowJoinTransportMenu(TitleScreen screen)
        {
            _steamFriendJoinPageActive = true;
            _inHostStatusMenu = false;
            _inClientWaitingMenu = false;

            Action back = () =>
            {
                _steamFriendJoinPageActive = false;
                ShowMultiplayerMenu(screen);
            };

            BuildCoopScreen(screen, MenuJoin, CoopText.T("Join a friend"), back, lobby: false, () =>
            {
                if (_steamFriendLobbies.Count == 0)
                    CoopMenu.Info(CoopText.T("No friend has an open lobby right now. The list updates by itself."), CoopMenu.MutedColor);
                else
                    CoopMenu.Info(CoopText.T("Friends with an open lobby:"));
                CoopMenu.Gap();

                foreach (var friendLobby in _steamFriendLobbies)
                {
                    var lobbyId = friendLobby.LobbyId;
                    var hostSteamId = friendLobby.HostSteamId;
                    var name = string.IsNullOrWhiteSpace(friendLobby.PersonaName)
                        ? CoopText.T("Steam friend")
                        : friendLobby.PersonaName.Trim();

                    CoopMenu.Item(
                        name,
                        () =>
                        {
                            _steamFriendJoinPageActive = false;
                            HandleSteamFriendLobbyJoinRequest(lobbyId, hostSteamId);
                        },
                        CoopText.F("Join {0}'s lobby", name),
                        color: CoopMenu.AccentColor);
                }

                if (_steamFriendLobbies.Count > 0)
                    CoopMenu.Gap();

                CoopMenu.Item(
                    CoopText.T("Open Steam friends"),
                    () => OpenSteamFriendsJoinOverlay(screen),
                    CoopText.T("Pick a friend in Steam and choose Join Game."));
                CoopMenu.Item(CoopText.T("Back"), back);
            });

            RequestSteamFriendLobbyRefresh(force: _steamFriendLobbies.Count == 0);
        }

        internal static void ShowOtherWaysMenu(TitleScreen screen)
        {
            _steamFriendJoinPageActive = false;
            _inHostStatusMenu = false;
            _inClientWaitingMenu = false;

            BuildCoopScreen(screen, MenuOther, CoopText.T("Other ways to connect"), () => ShowMultiplayerMenu(screen), lobby: false, () =>
            {
                CoopMenu.Item(
                    CoopText.T("Create public lobby"),
                    () => StartSteamHost(screen, SteamConnect.SteamLobbyVisibility.Public),
                    CoopText.T("Anyone with the lobby code can join. The code is copied to the clipboard."));
                CoopMenu.Item(
                    CoopText.T("Join by lobby code"),
                    () => StartSteamJoin(screen),
                    CoopText.T("Copy the lobby code first, then choose this."));
                CoopMenu.Gap();
                CoopMenu.Item(
                    CoopText.T("Local network: host"),
                    () => ShowConnectionMenu(screen, NetRole.Host),
                    CoopText.T("Players on the same network connect by your IP."));
                CoopMenu.Item(
                    CoopText.T("Local network: join"),
                    () => ShowConnectionMenu(screen, NetRole.Client),
                    CoopText.T("Connect by the host's IP and port."));
                CoopMenu.Gap();
                CoopMenu.Item(CoopText.T("Back"), () => ShowMultiplayerMenu(screen));
            });
        }

        internal static void ShowConnectionMenu(TitleScreen screen, NetRole role)
        {
            _menuSelection = role;
            _menuTransport = ConnectionTransport.Lan;
            _inHostStatusMenu = false;
            _inClientWaitingMenu = false;
            if (role == NetRole.Host)
                SetRole(NetRole.None);

            var isHost = role == NetRole.Host;
            var title = isHost ? CoopText.T("Local network: host") : CoopText.T("Local network: join");

            BuildCoopScreen(screen, MenuLan, title, () => ShowOtherWaysMenu(screen), lobby: false, () =>
            {
                CoopMenu.Info(isHost
                    ? CoopText.T("Share your IP and port with players on the same network.")
                    : CoopText.T("Enter the host's IP and port."));
                CoopMenu.Gap();
                CoopMenu.Item(CoopText.F("Name: {0}", _username), () => EditUsername(screen), CoopText.T("Your name in the lobby."));
                CoopMenu.Item(CoopText.F("IP: {0}", _mpIp), () =>
                {
                    OpenTextInput(screen, CoopText.T("IP address"), _mpIp, value =>
                    {
                        _mpIp = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value;
                        SaveConfig();
                        ShowConnectionMenu(screen, role);
                    }, noSpaces: true);
                }, CoopText.T("Change the IP address."));
                CoopMenu.Item(CoopText.F("Port: {0}", _mpPort), () =>
                {
                    OpenTextInput(screen, CoopText.T("Port"), _mpPort.ToString(CultureInfo.InvariantCulture), value =>
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > 65535)
                            parsed = 1234;
                        _mpPort = parsed;
                        SaveConfig();
                        ShowConnectionMenu(screen, role);
                    }, noSpaces: true);
                }, CoopText.T("Change the port."));
                CoopMenu.Gap();

                if (isHost)
                {
                    CoopMenu.Item(CoopText.T("Create lobby"), () =>
                    {
                        StartHostServerOnly();
                        ShowHostStatusMenu(screen);
                    }, CoopText.T("Start hosting on this IP and port."), color: CoopMenu.AccentColor);
                }
                else
                {
                    CoopMenu.Item(CoopText.T("Connect"), () =>
                    {
                        StartNetwork(role, screen);
                        ShowClientWaitingMenu(screen);
                    }, CoopText.T("Connect to the host."), color: CoopMenu.AccentColor);
                }

                CoopMenu.Item(CoopText.T("Back"), () => ShowOtherWaysMenu(screen));
            });
        }

        internal static void ShowHostStatusMenu(TitleScreen screen)
        {
            if (_menuRebuildDepth > 0)
                return;
            _menuRebuildDepth++;
            try
            {
                CoopDlcSession.OnHostingStarted();
                if (!ReadSessionSnapshot().LocalReady)
                    SetLocalReady(true, sendToRemote: true, refreshMenu: false);

                _inHostStatusMenu = true;
                _inClientWaitingMenu = false;
                ConnectionUI.NotifyConnectionsChanged();

                var canLaunch = AllPlayersReady();
                var hasContinue = HasLocalContinueSaveState(out _);
                var continueOk = IsHostContinueCompatible(out var continueReason);
                var waitingHelp = CoopText.T("Waiting until every player is ready.");

                BuildCoopScreen(screen, MenuHostLobby, CoopText.T("Lobby"), back: null, lobby: true, () =>
                {
                    var (status, statusColor) = GetHostLobbyStatus(canLaunch);
                    CoopMenu.Info(status, statusColor);
                    CoopMenu.Gap();

                    CoopMenu.Item(
                        CoopText.T("New run"),
                        () => StartHostRunNormalMode(screen),
                        canLaunch ? CoopText.T("Start a new co-op run.") : waitingHelp,
                        canLaunch,
                        canLaunch ? CoopMenu.AccentColor : null);

                    if (hasContinue)
                    {
                        var canContinue = canLaunch && continueOk;
                        CoopMenu.Item(
                            GetContinueButtonLabel(screen),
                            () => ContinueHostRun(screen),
                            canContinue
                                ? CoopText.T("Continue the multiplayer save.")
                                : canLaunch ? CoopText.T(continueReason) : waitingHelp,
                            canContinue);
                    }

                    CoopMenu.Item(
                        CoopText.T("Custom mode"),
                        () => OpenHostCustomMode(screen),
                        canLaunch ? CoopText.T("Set up and start a custom mode run.") : waitingHelp,
                        canLaunch);
                    CoopMenu.Gap();

                    if (_menuTransport == ConnectionTransport.Steam && _steamLobbyId != 0UL)
                    {
                        CoopMenu.Item(
                            CoopText.T("Invite a friend"),
                            () => OpenSteamHostInviteOverlay(screen),
                            CoopText.T("Open the Steam invite list."));

                        if (_steamLobbyVisibility == SteamConnect.SteamLobbyVisibility.Public)
                        {
                            CoopMenu.Item(
                                CoopText.T("Copy lobby code"),
                                () =>
                                {
                                    if (TryCopySteamLobbyCodeFromUi())
                                        MultiplayerUI.PushSystemMessage(CoopText.T("Lobby code copied to clipboard"));
                                },
                                GetSteamLobbyCodeForUi());
                        }
                    }

                    CoopMenu.Item(GetMultiplayerSaveButtonLabel(), () => OpenMultiplayerSlotMenu(screen), CoopText.T("Choose the multiplayer save slot."));
                    CoopMenu.Item(GetDlcMenuLabel(), () => ShowHostDlcMenu(screen), CoopText.T("Choose which DLC are used in this session."));
                    CoopMenu.Item(
                        CoopText.F("Difficulty: {0}", GetDifficultyName(CoopBalanceSync.HostDifficulty)),
                        () =>
                        {
                            CoopBalanceSync.CycleHostDifficulty();
                            ShowHostStatusMenu(screen);
                        },
                        DescribeDifficulty(CoopBalanceSync.HostDifficulty));
                    CoopMenu.Gap();
                    CoopMenu.Item(CoopText.T("Close lobby"), () => CloseHostLobby(screen), CoopText.T("Disconnect everyone and go back."));
                });
            }
            finally
            {
                _menuRebuildDepth--;
            }
        }

        static (string Text, int Color) GetHostLobbyStatus(bool canLaunch)
        {
            RefreshPlayersDisplayFromNetwork();
            var others = _playersDisplay.Where(p => !p.IsHost).ToList();
            if (others.Count == 0)
            {
                return _menuTransport == ConnectionTransport.Steam
                    ? (CoopText.T("Waiting for a friend. Invite someone from Steam."), CoopMenu.InfoColor)
                    : (CoopText.T("Waiting for players on the local network."), CoopMenu.InfoColor);
            }

            if (canLaunch)
                return (CoopText.T("Everyone is ready. Start the run!"), CoopMenu.GoodColor);

            var waiting = string.Join(", ", others.Where(p => !p.Ready).Select(p => p.Name));
            return (CoopText.F("Waiting for: {0}", waiting), CoopMenu.InfoColor);
        }

        static void CloseHostLobby(TitleScreen screen)
        {
            var transport = _menuTransport;
            StopNetworkFromMenu();
            SetRole(NetRole.None);
            _menuSelection = NetRole.None;
            _inHostStatusMenu = false;
            if (transport == ConnectionTransport.Lan)
                ShowConnectionMenu(screen, NetRole.Host);
            else
                ShowMultiplayerMenu(screen);
        }

        internal static void ShowClientWaitingMenu(TitleScreen screen)
        {
            if (_menuRebuildDepth > 0)
                return;
            _menuRebuildDepth++;
            try
            {
                _inClientWaitingMenu = true;
                _inHostStatusMenu = false;
                ConnectionUI.NotifyConnectionsChanged();

                var ready = ReadSessionSnapshot().LocalReady;
                BuildCoopScreen(screen, MenuClientLobby, CoopText.T("Lobby"), back: null, lobby: true, () =>
                {
                    CoopMenu.Info(
                        ready ? CoopText.T("Waiting for the host to start the run.") : CoopText.T("Mark yourself ready when you are set."),
                        ready ? CoopMenu.GoodColor : CoopMenu.InfoColor);
                    CoopMenu.Gap();
                    CoopMenu.Item(
                        GetReadyButtonLabel(),
                        () => ToggleLocalReadyFromMenu(screen),
                        CoopText.T("Toggle your ready state."),
                        color: ready ? CoopMenu.GoodColor : CoopMenu.AccentColor);
                    CoopMenu.Item(GetMultiplayerSaveButtonLabel(), () => OpenMultiplayerSlotMenu(screen), CoopText.T("Choose the multiplayer save slot."));
                    CoopMenu.Gap();
                    CoopMenu.Item(CoopText.T("Leave lobby"), () => LeaveClientLobby(screen), CoopText.T("Disconnect and go back."));
                });
            }
            finally
            {
                _menuRebuildDepth--;
            }
        }

        static void LeaveClientLobby(TitleScreen screen)
        {
            var transport = _menuTransport;
            DisconnectFromMenu(screen, restoreTitle: false);
            if (transport == ConnectionTransport.Lan)
                ShowConnectionMenu(screen, NetRole.Client);
            else
                ShowJoinTransportMenu(screen);
        }

        internal static void ShowHostDlcMenu(TitleScreen screen)
        {
            if (_menuRebuildDepth > 0)
                return;
            _menuRebuildDepth++;
            try
            {
                _inHostStatusMenu = false;
                _inClientWaitingMenu = false;

                BuildCoopScreen(screen, MenuDlc, CoopText.T("Session DLC"), () => ShowHostStatusMenu(screen), lobby: true, () =>
                {
                    CoopMenu.Info(CoopText.T("Only DLC that every player owns can be enabled."));
                    if (_inActualRun)
                        CoopMenu.Info(CoopText.T("A run is in progress: changes apply to the next run."), CoopMenu.WarningColor);
                    CoopMenu.Gap();

                    foreach (var choice in CoopDlcSession.DescribeHostChoices())
                    {
                        var bit = choice.Dlc.Bit;
                        var name = CoopText.DlcName(bit, choice.Dlc.Name);
                        if (choice.Available)
                        {
                            CoopMenu.Item(
                                CoopText.F("{0}: {1}", name, choice.Enabled ? CoopText.T("On") : CoopText.T("Off")),
                                () =>
                                {
                                    CoopDlcSession.ToggleHostPreference(bit);
                                    ShowHostDlcMenu(screen);
                                },
                                CoopText.T("Toggle this DLC for the session."),
                                color: choice.Enabled ? null : CoopMenu.MutedColor);
                        }
                        else
                        {
                            CoopMenu.Item(
                                CoopText.F("{0}: {1}", name, CoopText.T("unavailable")),
                                () => { },
                                CoopText.F("Not owned by: {0}", choice.MissingFor),
                                enabled: false);
                        }
                    }

                    CoopMenu.Gap();
                    CoopMenu.Item(CoopText.T("Back"), () => ShowHostStatusMenu(screen));
                });
            }
            finally
            {
                _menuRebuildDepth--;
            }
        }

        internal static void ShowLobbyNotFoundPopup(TitleScreen screen)
        {
            ShowConnectionErrorPopup(
                screen,
                CoopText.T("Can't find lobby"),
                CoopText.T("The lobby is closed or the code is wrong."),
                () => ShowJoinTransportMenu(screen));
        }

        internal static void ShowConnectionErrorPopup(TitleScreen screen, string title, string details, Action onOk)
        {
            _inClientWaitingMenu = false;
            _inHostStatusMenu = false;
            _steamFriendJoinPageActive = false;

            BuildCoopScreen(screen, MenuMessage, string.Empty, onOk, lobby: false, () =>
            {
                CoopMenu.Header(title, CoopMenu.ErrorColor);
                if (!string.IsNullOrWhiteSpace(details))
                    CoopMenu.Info(details);
                CoopMenu.Gap();
                CoopMenu.Item(CoopText.T("OK"), onOk);
            });
        }

        internal static void PrepareSteamJoinConnectionUiOnly(TitleScreen screen)
        {
            _inClientWaitingMenu = false;
            _inHostStatusMenu = false;
            BuildCoopScreen(screen, MenuClientLobby, CoopText.T("Lobby"), back: null, lobby: true, () =>
            {
                CoopMenu.Info(CoopText.T("Connecting to the lobby..."), CoopMenu.InfoColor);
            });
            ConnectionUI.NotifyConnectionsChanged();
        }

        internal static string GetSessionDlcLabel()
        {
            var mask = CoopDlcSession.SessionMask;
            if (mask < 0)
                return CoopText.T("DLC: waiting for host");
            return CoopText.F("DLC: {0}", DescribeDlcMask(mask));
        }

        static string DescribeDlcMask(int mask)
        {
            mask = CoopDlcMask.Normalize(mask);
            if (mask == 0)
                return CoopText.T("none");

            var names = new List<string>(CoopDlcMask.Known.Length);
            foreach (var dlc in CoopDlcMask.Known)
            {
                if ((mask & dlc.Bit) != 0)
                    names.Add(CoopText.DlcName(dlc.Bit, dlc.Name));
            }
            return string.Join(", ", names);
        }

        static string GetDlcMenuLabel()
        {
            var choices = CoopDlcSession.DescribeHostChoices();
            var available = choices.Count(c => c.Available);
            var enabled = choices.Count(c => c.Enabled);
            if (available == 0 || enabled == 0)
                return CoopText.F("DLC: {0}", CoopText.T("none"));
            if (enabled == available)
                return CoopText.F("DLC: {0}", CoopText.T("all"));
            return CoopText.F("DLC: {0} of {1}", enabled, available);
        }

        internal static string GetDifficultyName(CoopDifficulty difficulty) => difficulty switch
        {
            CoopDifficulty.Relaxed => CoopText.T("Relaxed"),
            CoopDifficulty.Hard => CoopText.T("Hardcore"),
            _ => CoopText.T("Standard")
        };

        static string DescribeDifficulty(CoopDifficulty difficulty)
        {
            var values = CoopBalanceSync.PreviewValues(difficulty);
            var players = Math.Max(2, CoopBalanceSync.HostPlayerCount);
            return CoopText.F(
                "For {0} players: mob health x{1}, boss health x{2}, mob damage x{3}.",
                players,
                FormatMultiplier(values.MobHp),
                FormatMultiplier(values.BossHp),
                FormatMultiplier(values.MobDamage));
        }

        static string FormatMultiplier(double value)
            => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        internal static IReadOnlyList<LobbyPanelLine> GetLobbyPanelLines()
        {
            var lines = new List<LobbyPanelLine>(5);
            var state = ReadSessionSnapshot();

            if (state.Role == NetRole.Client)
                lines.Add(new LobbyPanelLine(CoopText.F("Mode: {0}", GetPendingLaunchSummaryLabel(GetTitleScreen())), CoopMenu.InfoColor));

            lines.Add(new LobbyPanelLine(GetSessionDlcLabel(), CoopMenu.InfoColor));
            lines.Add(new LobbyPanelLine(
                CoopBalanceSync.HasHostDifficulty
                    ? CoopText.F("Difficulty: {0}", GetDifficultyName(CoopBalanceSync.SessionDifficulty))
                    : CoopText.T("Difficulty: waiting for host"),
                CoopMenu.InfoColor));

            if (_menuTransport == ConnectionTransport.Lan)
            {
                lines.Add(new LobbyPanelLine(CoopText.F("Local network: {0}:{1}", _mpIp, _mpPort), CoopMenu.MutedColor));
            }
            else if (_steamLobbyVisibility == SteamConnect.SteamLobbyVisibility.Public && state.Role == NetRole.Host)
            {
                var code = GetSteamLobbyCodeForUi();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    code = code.Trim().ToLowerInvariant();
                    lines.Add(new LobbyPanelLine(CoopText.F("Lobby code: {0}", code), CoopMenu.AccentColor, IsCode: true, Value: code));
                    lines.Add(new LobbyPanelLine(CoopText.T("Click the code to copy it."), CoopMenu.MutedColor));
                }
            }

            return lines;
        }

        internal static string GetLobbyPanelSignature()
        {
            var lines = GetLobbyPanelLines();
            return string.Join("\u001f", lines.Select(l => l.Text));
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP MENUS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Все экраны мультиплеера на титульнике, строятся через CoopMenu (родное меню игры).
 *  Кооператив: Создать лобби (Steam, только друзья) · Присоединиться · Другие способы · Назад.
 *  Присоединиться: открытые лобби друзей (обновляется само) · Друзья Steam.
 *  Другие способы: публичное лобби · вход по коду · локальная сеть (хост/клиент).
 *  Лобби хоста: Новый забег · Продолжить (если есть MP-сейв) · Свой режим · Пригласить · слот · DLC ·
 *  Сложность (по кругу Лёгкая → Стандарт → Хардкор, в подсказке множители для текущего числа игроков) · Закрыть.
 *  Хост готов автоматически; запуск открыт, когда готовы все. Лобби гостя: Готов · слот · Выйти.
 *  В лобби Esc/B не закрывает сессию (back = null) — только явным пунктом.
 *  Справа (ConnectionUI) — игроки и строки GetLobbyPanelLines: режим, DLC, сложность, адрес LAN / код публичного лобби.
 * ═══════════════════════════════════════
*/
