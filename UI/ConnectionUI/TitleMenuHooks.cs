using System.Reflection;
using dc.pr;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using ModCore.Modules;
using DeadCellsMultiplayerMod.UI;
using DeadCellsMultiplayerMod.Tools;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using DeadCellsMultiplayerMod.I18n;

namespace DeadCellsMultiplayerMod
{
    internal static partial class LobbySession
    {
        internal static void InitializeMenuUiHooks()
        {
            if (_menuHooksAttached) return;

            try
            {
                LoadConfig();
                DailyLeaderboardGuard.Initialize();
                MultiplayerSaves.InitializeMultiplayerSaveHooks();
                InitializeMultiplayerLaunchHooks();
                Hook_TitleScreen.mainMenu += MainMenuHook;
                _menuHooksAttached = true;
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] TitleScreen hooks failed: {Message}", ex.Message);
            }
        }

        internal static void MainMenuHook(Hook_TitleScreen.orig_mainMenu orig, TitleScreen self)
        {
            if (CoopMenu.TryHandleBack(self))
                return;

            CoopMenu.Close();
            ModEntry.PumpSteamCallbacksForOverlay();
            if (!_addMenuHookRegistered)
            {
                Hook_TitleScreen.addMenu += AddMenuHook;
                _addMenuHookRegistered = true;
            }
            TryDisconnectWhenReturningToMainMenu();
            StoreTitleScreen(self);
            _mainMenuButtonAdded = false;
            // Ensure a live ConnectionUI before any visibility toggle: returning mid-run
            // destroys the previous TitleScreen tree and leaves a stale Instance.root.
            ConnectionUI.EnsureCreated(self);
            ResetOriginalMainMenuUiState();
            ConnectionUI.set_visible = false;
            orig(self);

            try { _ConnectionUI.RememberLocalHeroSkinFromUser(self.user, "titleScreen.mainMenu"); } catch { }

            EnsureMainMenuMultiplayerButton(self);
            ProcessPendingOverlayJoinRequest(self);
        }

        internal static void ResetOriginalMainMenuUiState()
        {
            ResetHostDisconnectCountdown();
            _inHostStatusMenu = false;
            _inClientWaitingMenu = false;
            _menuSelection = NetRole.None;
            ConnectionUI.set_visible = false;
        }

        internal static void ProcessPendingOverlayJoinRequest(TitleScreen screen)
        {
            if (_pendingOverlayJoinLobbyId is not { } lobbyId)
                return;
            _pendingOverlayJoinLobbyId = null;
            _log?.Information("[NetMod][Steam] Processing queued overlay join request (lobbyId={LobbyId})", lobbyId);
            HandleSteamOverlayJoinRequest(lobbyId);
        }

        internal static void TryDisconnectWhenReturningToMainMenu()
        {
            if (_role == NetRole.None)
                return;
            if (!_inActualRun)
                return;
            _log?.Information("[NetMod] Main menu opened during run; stopping network");
            StopNetworkFromMenu();
        }

        internal static virtual_cb_help_inter_isEnable_t_<bool> AddMenuHook(
            Hook_TitleScreen.orig_addMenu orig,
            TitleScreen self,
            dc.String str,
            HlAction cb,
            dc.String help,
            bool? isEnable,
            Ref<int> color)
        {
            if (_suppressAutoButton)
                return orig(self, str, cb, help, isEnable, color);

            ModEntry.PumpSteamCallbacksForOverlay();
            MainThreadPump.ProcessMainThreadQueue();
            var wrappedCb = WrapQuitCallbackIfNeeded(str, cb);
            var ret = orig(self, str, wrappedCb ?? cb, help, isEnable, color);

            try
            {
                if (_suppressAutoButton) return ret;
                if (_mainMenuButtonAdded) return ret;
                if (!self.isMainMenu) return ret;

                var items = TitleScreenReflection.GetMemberValue(self, "menuItems", true);
                if (items == null)
                    return ret;
                var count = TitleScreenReflection.GetArrayLength(items);
                if (count == 1)
                {
                    int itemColor = 0xFFFFFF;
                    var label = CoopText.T("Co-op").AsHaxeString();
                    var helpStr = CoopText.T("Play together with your Steam friends.").AsHaxeString();
                    var colorHl = Ref<int>.From(ref itemColor);
                    var cbHl = new HlAction(() => ShowMultiplayerMenu(self));
                    var item = orig(self, label, cbHl, helpStr, null, colorHl);
                    CoopText.Fit(item.t);
                    _mainMenuButtonAdded = true;
                }
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] addMenu hook failed: {Message}", ex.Message);
            }

            return ret;
        }

        internal static HlAction? WrapQuitCallbackIfNeeded(dc.String label, HlAction? callback)
        {
            if (callback == null)
                return null;
            if (_role == NetRole.None)
                return callback;

            var text = label?.ToString() ?? string.Empty;
            if (!IsQuitMenuLabel(text))
                return callback;

            return new HlAction(() =>
            {
                try { StopNetworkFromMenu(); }
                catch (Exception ex) { _log?.Warning("[NetMod] Quit cleanup failed: {Message}", ex.Message); }
                try { callback(); }
                catch (Exception ex) { _log?.Warning("[NetMod] Quit callback failed: {Message}", ex.Message); }
            });
        }

        internal static bool IsQuitMenuLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;
            var text = label.Trim();
            if (text.IndexOf("quit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text.IndexOf("exit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text.IndexOf("РІС‹Р№С‚", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text.IndexOf("РІС‹С…РѕРґ", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            try
            {
                var localizedQuit = GetText.Instance.GetString("Quitter le jeu");
                if (!string.IsNullOrWhiteSpace(localizedQuit) &&
                    string.Equals(text, localizedQuit, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            return false;
        }

        internal static void EnsureMainMenuMultiplayerButton(TitleScreen screen)
        {
            try
            {
                var arr = TitleScreenReflection.GetMemberValue(screen, "menuItems", true);
                var coopLabel = CoopText.T("Co-op");
                var coopHelp = CoopText.T("Play together with your Steam friends.");
                var playLabel = CoopText.Game("Jouer");
                var existingIdx = TitleScreenReflection.FindMenuIndexByLabel(arr, coopLabel);
                if (existingIdx < 0)
                {
                    TryAddMenuButton(
                        screen,
                        coopLabel,
                        () => ShowMultiplayerMenu(screen),
                        coopHelp);
                    arr = TitleScreenReflection.GetMemberValue(screen, "menuItems", true);
                }
                _mainMenuButtonAdded = true;
                MoveButtonAfterPlay(screen, arr, coopLabel, playLabel);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to ensure main menu button order: {Message}", ex.Message);
            }
        }

        internal static void MoveButtonAfterPlay(TitleScreen screen, object? arrObj, string targetLabel, string anchorLabel)
        {
            if (arrObj == null) return;
            try
            {
                var type = arrObj.GetType();
                var getDyn = type.GetMethod("getDyn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var removeDyn = type.GetMethod("removeDyn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var insertDyn = type.GetMethod("insertDyn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getDyn == null || removeDyn == null || insertDyn == null) return;

                int len = TitleScreenReflection.GetArrayLength(arrObj);
                int targetIdx = -1;
                int anchorIdx = -1;
                object? targetObj = null;

                for (int i = 0; i < len; i++)
                {
                    var item = getDyn.Invoke(arrObj, new object[] { i });
                    var label = TitleScreenReflection.GetMenuLabel(item);
                    if (targetIdx < 0 && label.Equals(targetLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIdx = i;
                        targetObj = item;
                    }
                    if (anchorIdx < 0 && label.Equals(anchorLabel, StringComparison.OrdinalIgnoreCase))
                        anchorIdx = i;
                }

                if (targetIdx < 0 || anchorIdx < 0 || targetObj == null) return;
                var desired = anchorIdx + 1;
                if (targetIdx == desired) return;

                removeDyn.Invoke(arrObj, new[] { targetObj });
                insertDyn.Invoke(arrObj, new object[] { desired, targetObj });
                MoveMenuTextAfterAnchor(screen, arrObj, getDyn, targetObj, desired);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to reposition menu button: {Message}", ex.Message);
            }
        }

        static void MoveMenuTextAfterAnchor(TitleScreen screen, object arrObj, MethodInfo getDyn, object targetObj, int index)
        {
            try
            {
                var wrapper = screen.menuItemsWrapper;
                var targetText = TitleScreenReflection.GetMemberValue(targetObj, "t", true) as dc.h2d.Object;
                if (wrapper == null || targetText == null || index <= 0)
                    return;

                var anchorItem = getDyn.Invoke(arrObj, new object[] { index - 1 });
                var anchorText = TitleScreenReflection.GetMemberValue(anchorItem, "t", true) as dc.h2d.Object;
                if (anchorText == null)
                    return;

                var anchorPos = wrapper.getChildIndex(anchorText);
                if (anchorPos < 0)
                    return;

                wrapper.addChildAt(targetText, anchorPos + 1);
                wrapper.reflow();
                screen.select(screen.curMenu, Ref<bool>.Null);
            }
            catch (Exception ex)
            {
                _log?.Debug("[NetMod] Failed to reorder menu text: {Message}", ex.Message);
            }
        }
    }
}
