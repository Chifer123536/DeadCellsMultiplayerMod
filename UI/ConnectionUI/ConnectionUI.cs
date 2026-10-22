using dc;
using dc.h2d;
using dc.hxd;
using dc.libs.heaps.slib;
using dc.pr;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Utilities;
using Serilog;
using DeadCellsMultiplayerMod.I18n;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection.LightingInitializer;
using DeadCellsMultiplayerMod.Tools;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.Connection
{
    public partial class ConnectionUI :
    Process,
    IEventReceiver
    {
        static readonly int PanelInner = 0x15120F;
        static readonly int PanelInnerEdge = 0x3A3226;
        static readonly int PanelInnerTop = 0x4A4030;
        static readonly int AccentColor = CoopMenu.AccentColor;
        static readonly int TextColor = 0xE6E1D6;
        static readonly int HelpColor = 0x9E978A;
        static readonly int HoverBorderColor = CoopMenu.AccentColor;
        static readonly int SeatFill = 0x000000;
        const double ButtonCornerRadius = 10.0;
        const double FieldCornerRadius = 8.0;
        const double CardCornerRadius = 12.0;
        const double LobbyBackdropAlpha = 0.42;
        const double SeatAlpha = 0.28;

        const double WindowedTextBoost = 1.6;
        const int WindowedDisplayMode = 0;
        const int FullscreenDisplayMode = 1;
        const int BorderlessDisplayMode = 2;
        static int _cachedDisplayMode = int.MinValue;
        static int _cachedFullScreenMode = int.MinValue;
        static double _cachedTextBoost = 1.0;

        const int LayoutMetricProbeIntervalFrames = 10;

        Flow? spritesflow;
        readonly List<HSprite> sprites = new();
        readonly List<dc.ui.Text> connectionLabels = new();
        HSprite? spriteui;

        dc.h2d.Object? _panelRoot;
        dc.h2d.Object? _lobbyPanelRoot;
        dc.h2d.Interactive? _codeHit;
        bool _lobbyVisible;
        string _lastLobbySignature = string.Empty;
        string _lobbyCode = string.Empty;
        int _layoutW = 1920;
        int _layoutH = 1080;
        double _lastLayoutUiScale = double.NaN;
        double _lastLayoutTextBoost = double.NaN;
        int _layoutMetricProbeCountdown;

        static ConnectionUI? Instance;
        readonly LifecycleTracker _lifecycle = new("ConnectionUI");
        readonly long _lifecycleGeneration;

        public ConnectionUI(Process parent) : base(parent)
        {
            _lifecycleGeneration = _lifecycle.Start();
            Instance = this;
            this.createRoot(parent.root);
            _ = new MainPageLightingInitializer(this);
            this.onResize();
            EventSystem.AddReceiver(this);
            this.root.visible = set_visible;
        }

        public static bool set_visible
        {
            get
            {
                var instance = TryGetLiveInstance();
                return instance?.root?.visible ?? false;
            }
            set
            {
                var instance = TryGetLiveInstance();
                if (instance?.root == null)
                    return;

                try
                {
                    instance.root.visible = value;
                }
                catch
                {
                    Instance = null;
                }
            }
        }

        static ConnectionUI? TryGetLiveInstance()
        {
            var instance = Instance;
            if (instance == null)
                return null;

            try
            {
                if (!instance._lifecycle.IsCurrent(instance._lifecycleGeneration) ||
                    instance.root == null || instance.destroyed)
                {
                    Instance = null;
                    return null;
                }
            }
            catch
            {
                Instance = null;
                return null;
            }

            return instance;
        }

        static double GetWindowedTextBoost()
        {
            try
            {
                var win = dc.hxd.Window.Class.getInstance();
                if (win == null)
                    return _cachedTextBoost;

                var displayMode = int.MinValue;
                var sdlWin = win.window;
                if (sdlWin != null)
                    displayMode = sdlWin.displayMode;

                var mode = win.fullScreenMode;
                if (_cachedDisplayMode == displayMode && _cachedFullScreenMode == mode)
                    return _cachedTextBoost;

                _cachedDisplayMode = displayMode;
                _cachedFullScreenMode = mode;

                if (displayMode == FullscreenDisplayMode || displayMode == BorderlessDisplayMode)
                    _cachedTextBoost = 1.0;
                else if (displayMode == WindowedDisplayMode)
                    _cachedTextBoost = WindowedTextBoost;
                else if (mode == FullscreenDisplayMode || mode == BorderlessDisplayMode)
                    _cachedTextBoost = 1.0;
                else
                    _cachedTextBoost = WindowedTextBoost;

                return _cachedTextBoost;
            }
            catch
            {
                return _cachedTextBoost;
            }
        }

        public static void RefreshLayoutAfterDisconnect()
        {
            try
            {
                var instance = TryGetLiveInstance();
                if (instance != null && set_visible)
                    instance.onResize();
            }
            catch
            {
            }
        }

        public static void ShowLobby(bool visible)
        {
            var instance = TryGetLiveInstance();
            if (instance == null)
                return;

            _ConnectionUI.InvalidateLobbyPlayerSlots();
            instance._lobbyVisible = visible;
            instance._lastLobbySignature = string.Empty;
            set_visible = true;
            instance.onResize();
        }

        public static void ShowLobbyMode() => ShowLobby(true);

        public static void DismissAndHide()
        {
            var instance = TryGetLiveInstance();
            if (instance != null)
            {
                instance._lobbyVisible = false;
                instance.CloseTextPrompt(apply: false);
                instance.ClearLobbyPanel();
            }
            set_visible = false;
        }

        public void updateConnections()
        {
            if (!this._lobbyVisible)
                return;

            var slots = _ConnectionUI.GetLobbyPlayerSlots();
            var signature = _ConnectionUI.BuildLobbySlotsSignature(slots) + "\u001e" + LobbySession.GetLobbyPanelSignature();
            if (string.Equals(signature, this._lastLobbySignature, StringComparison.Ordinal))
                return;

            RebuildLobbyPanel(slots, signature);
        }

        public static void NotifyConnectionsChanged()
        {
            _ConnectionUI.InvalidateLobbyPlayerSlots();
            TryGetLiveInstance()?.updateConnections();
        }

        void RebuildLobbyPanel(List<_ConnectionUI.LobbyPlayerSlot> slots, string signature)
        {
            if (this._panelRoot == null)
                return;

            ClearLobbyPanel();
            this._lastLobbySignature = signature;

            var uiScale = UiScale.GetResolutionScale();
            double textUi = System.Math.Max(uiScale, 1.0) * GetWindowedTextBoost() * 1.35;
            double screenPad = 28.0 * uiScale;
            double pad = 18.0 * uiScale;

            this._lobbyPanelRoot = new dc.h2d.Object(null);
            this._panelRoot.addChild(this._lobbyPanelRoot);
            var backdrop = new Graphics(this._lobbyPanelRoot);
            var content = new dc.h2d.Object(this._lobbyPanelRoot);

            var row = PlaceLobbyBeheaded(slots, content, uiScale, textUi);
            double y = row.Bottom + 10.0 * uiScale;
            double width = row.Width;

            this._lobbyCode = string.Empty;
            double codeY = -1;
            foreach (var line in LobbySession.GetLobbyPanelLines())
            {
                var text = Assets.Class.makeText(line.Text.AsHaxeString(), line.Color, false, content);
                text.customScale = (line.IsCode ? 0.52 : 0.42) * textUi;
                text.onResize();
                text.textColor = line.Color;
                CoopText.Fit(text);
                text.x = 0;
                text.y = y;
                width = System.Math.Max(width, MeasureWidth(text));
                if (line.IsCode)
                {
                    this._lobbyCode = line.Value;
                    codeY = y;
                }
                y += MeasureHeight(text, 22.0 * uiScale) + 4.0 * uiScale;
                this.connectionLabels.Add(text);
            }

            double contentH = y;
            UiChrome.FillRoundRect(backdrop, -pad, -pad, width + pad * 2.0, contentH + pad * 2.0, CardCornerRadius * uiScale, PanelInner, LobbyBackdropAlpha);

            this._lobbyPanelRoot.x = this._layoutW - width - pad - screenPad;
            this._lobbyPanelRoot.y = screenPad + pad;

            if (codeY >= 0 && this._lobbyCode.Length > 0)
            {
                this._codeHit = new dc.h2d.Interactive(width, 26.0 * uiScale, content, null);
                this._codeHit.y = codeY;
                this._codeHit.onClick = new HlAction<Event>(_ =>
                {
                    if (LobbySession.TryCopySteamLobbyCodeFromUi())
                        MultiplayerUI.PushSystemMessage(CoopText.T("Lobby code copied to clipboard"));
                });
            }

            try { this._lobbyPanelRoot.set_visible(true); } catch { }
        }

        static void CenterMenuText(dc.ui.Text text, string label, double regionX, double regionW, double customScale)
        {
            var textWidth = MeasureWidth(text);
            if (textWidth <= 0)
                textWidth = label.Length * 7.0 * System.Math.Max(customScale, 0.4);

            text.x = System.Math.Max(regionX + 4.0, regionX + (regionW - textWidth) * 0.5);
        }

        static double MeasureWidth(dc.ui.Text text)
        {
            try
            {
                var w = text.textWidth;
                var sx = text.scaleX;
                return sx > 0.01 ? w * sx : w;
            }
            catch
            {
                return 0;
            }
        }

        static double MeasureHeight(dc.ui.Text text, double fallback)
        {
            try
            {
                var h = text.textHeight;
                var sy = text.scaleY;
                var value = sy > 0.01 ? h * sy : h;
                return value > 1.0 ? value : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        void ClearLobbyPanel()
        {
            ClearLobbyBeheadedSprites();
            try { this._codeHit?.remove(); } catch { }
            this._codeHit = null;
            try { this._lobbyPanelRoot?.remove(); } catch { }
            this._lobbyPanelRoot = null;
            this.connectionLabels.Clear();
            this._lastLobbySignature = string.Empty;
            this._lobbyCode = string.Empty;
        }

        public override void onResize()
        {
            base.onResize();
            if (base.root == null)
                return;

            var win = dc.hxd.Window.Class.getInstance();
            this._layoutW = System.Math.Max(1, win.get_width());
            this._layoutH = System.Math.Max(1, win.get_height());
            this._lastLayoutUiScale = UiScale.GetResolutionScale();
            this._lastLayoutTextBoost = GetWindowedTextBoost();
            this._layoutMetricProbeCountdown = LayoutMetricProbeIntervalFrames;

            ClearLobbyPanel();
            this._panelRoot?.remove();
            this._panelRoot = new dc.h2d.Object(null);
            this.root.addChild(this._panelRoot);

            if (this._lobbyVisible)
                updateConnections();

            if (this._promptOpen)
                RebuildTextPromptUi();
        }

        public override void update()
        {
            var perfEnabled = RuntimeHitchWatch.Enabled;
            var perfStart = perfEnabled ? RuntimeHitchWatch.Start() : 0L;
            base.update();

            if (this._layoutMetricProbeCountdown > 0)
            {
                this._layoutMetricProbeCountdown--;
            }
            else
            {
                this._layoutMetricProbeCountdown = LayoutMetricProbeIntervalFrames;
                try
                {
                    var win = dc.hxd.Window.Class.getInstance();
                    var liveUiScale = UiScale.GetResolutionScale();
                    var liveTextBoost = GetWindowedTextBoost();
                    if (win != null &&
                        (win.get_width() != this._layoutW ||
                         win.get_height() != this._layoutH ||
                         double.IsNaN(this._lastLayoutUiScale) ||
                         System.Math.Abs(liveUiScale - this._lastLayoutUiScale) > 0.001 ||
                         System.Math.Abs(liveTextBoost - this._lastLayoutTextBoost) > 0.001))
                    {
                        this.onResize();
                    }
                    else if (this._lobbyVisible)
                    {
                        updateConnections();
                    }
                }
                catch
                {
                }
            }

            TickTextPrompt();

            try { LobbyHeadFx.TickAll(1.0 / 60.0); } catch { }

            if (this._lobbyVisible)
                FlushPendingLobbyBeheadedSkinApply();

            try { TickLobbyHeadBones(); } catch { }

            if (perfEnabled)
            {
                var elapsedMs = RuntimeHitchWatch.GetElapsedMilliseconds(perfStart);
                if (elapsedMs >= RuntimeHitchWatch.ModFrameSlowThresholdMs)
                    RuntimeHitchWatch.LogSlow(Log.Logger, "ConnectionUI.Update", elapsedMs);
            }
        }

        public override void onDispose()
        {
            if (!_lifecycle.TryBeginStop())
            {
                base.onDispose();
                return;
            }

            try
            {
                CloseTextPrompt(apply: false);
                SetTitleControlsLocked(false);
                ClearLobbyPanel();
                this._panelRoot?.remove();
                this._panelRoot = null;
            }
            catch
            {
            }
            finally
            {
                _lifecycle.MarkDisposed();
                if (ReferenceEquals(Instance, this))
                    Instance = null;
                base.onDispose();
            }
        }

        public override void postUpdate()
        {
            base.postUpdate();
            if (this._lobbyBeheadedRoot != null)
                PushLobbyBeheadedLighting();
        }

        public static void Initialize(ModEntry entry)
        {
            entry.Logger.Information("\x1b[32m[[ModEntry.ConnectionUI] Initializing ConnectionUI...]\x1b[0m ");
        }

        public static void EnsureCreated(TitleScreen screen)
        {
            var live = TryGetLiveInstance();
            if (live != null && ReferenceEquals(live.parent, screen))
                return;

            Instance = null;
            var connectionUI = new ConnectionUI(screen);
            screen.addChild(connectionUI);
            try
            {
                connectionUI.root?.set_visible(false);
            }
            catch
            {
            }
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  CONNECTION UI — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Process поверх TitleScreen. Меню мультиплеера рисует сама игра (CoopMenu → addMenu);
 *  здесь только правая колонка лобби и окно ввода текста (ConnectionUI.TextPrompt).
 *  ShowLobby(true): колонка с героями игроков (ConnectionUI.LobbyBeheaded), строками
 *  LobbySession.GetLobbyPanelLines (режим, DLC, код лобби) на полупрозрачной тёмной подложке.
 *  Перестройка — при смене подписи слотов/строк (NotifyConnectionsChanged или проба раз в 10 кадров).
 *  Клик по строке кода лобби копирует код. Смена размера окна/режима → onResize → перестройка.
 * ═══════════════════════════════════════
*/
