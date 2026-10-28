using dc;
using dc.en;
using dc.en.mob;
using dc.h2d;
using dc.hxd;
using dc.libs._Cooldown;
using dc.pr;
using dc.tool;
using dc.ui;
using dc.ui.hud;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Utilities;
using Serilog;
using Cooldown = CooldownHelper.Cooldown;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using ModCore.Events.Interfaces.Game.Hero;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI
{

    public class MultiplayerUI :
        IEventReceiver,
        IOnAdvancedModuleInitializing,
        IOnHeroUpdate
    {
        enum PartyState
        {
            Alive,
            Downed,
            Out
        }

        sealed class LifeSlot
        {
            public int SlotIndex { get; }
            public FlowBox Container { get; }
            public dc.h2d.Object LabelBox { get; }
            public Graphics? ChipGraphics { get; set; }
            public dc.ui.Text? LabelText { get; set; }
            public dc.ui.Text? StatusText { get; set; }
            public string? LastLabel { get; set; }
            public string? LastStatus { get; set; }
            public int LastLife { get; set; } = int.MinValue;
            public int LastMaxLife { get; set; } = int.MinValue;
            public PartyState LastState { get; set; } = (PartyState)(-1);
            public bool LastSpectated { get; set; }
            public double Scale { get; set; }
            public double HeightArt { get; set; }
            public double RowTopArt { get; set; }
            public double RowHeightArt { get; set; }
            public double BarTopArt { get; set; }
            public double NameMaxWidthPx { get; set; }

            public LifeSlot(int slotIndex, FlowBox container, dc.h2d.Object labelBox)
            {
                SlotIndex = slotIndex;
                Container = container;
                LabelBox = labelBox;
            }
        }

        private ModEntry mod { get; set; }
        private dc.h2d.Flow toplib { get; set; } = null!;
        public static dc.h2d.Flow flowContainer = null!;
        private static NetNode? _net;
        private NetNode? _boundNet;
        public int SlotIndex { get; set; }

        private LifeSlot?[] _slots = System.Array.Empty<LifeSlot?>();
        private bool[] _slotActive = System.Array.Empty<bool>();
        private HUD? _hud;
        private long _hudReadyAfterTicks;

        private int lastLife = 0;
        private int lastMaxLife = 0;
        private int _lastHostConnectedClientCount = -1;

        // Throttle: KingLifeUpdate is hooked on updateLifeBar AND called from OnHeroUpdate, so
        // without this it ran twice per frame during combat. ~33ms keeps HUD response invisible.
        private long _lastKingLifeUpdateTicks;
        private static readonly long KingLifeUpdateMinIntervalTicks =
            System.Diagnostics.Stopwatch.Frequency / 30;

        private static MultiplayerUI? _instance;

        const double PlateW = 96.0;
        const double PlateEdge = 2.0;
        const double PlateAccentW = 2.0;
        const double PlatePadL = 3.0;
        const double PlatePadR = 3.0;
        const double PlatePadT = 3.0;
        const double PlatePadB = 3.0;
        const double PlateRowGap = 1.0;
        const double PlateBarH = 4.0;
        const double PlateGapY = 3.0;
        const double PlateNameStatusGap = 4.0;
        const double PlateContentL = PlateEdge + PlateAccentW + PlatePadL;
        const double PlateContentR = PlateW - PlateEdge - PlatePadR;
        const int PlateBarTicks = 4;

        const int ColorEdge = 0x07080D;
        const int ColorFrame = 0x343B52;
        const int ColorFrameSpectated = 0xE6B34C;
        const int ColorPanel = 0x131725;
        const int ColorPanelLight = 0x1F2538;
        const int ColorBarBack = 0x2B1419;
        const int ColorBarBackDowned = 0x3A2414;
        const int ColorBarBackOut = 0x1D1F26;
        const int ColorDowned = 0xFF8A3D;
        const int ColorOut = 0x5C606B;
        const int ColorName = 0xF1E7D0;
        const int ColorNameSpectated = 0xFFE3A0;
        const int ColorTextMuted = 0x8E93A0;

        const double SpectateHintBottomArt = 34.0;
        const int ColorSpectateHint = 0xF1E7D0;
        const int ColorSpectateHintSub = 0xB4B9C6;

        dc.ui.Text? _spectateHintTitle;
        dc.ui.Text? _spectateHintSub;
        string? _spectateHintTitleValue;
        string? _spectateHintSubValue;
        bool _previewActive;

        public MultiplayerUI(ModEntry Entry, int slotIndex = 0)
        {
            mod = Entry;
            SlotIndex = slotIndex;
            _instance = this;
            EventSystem.AddReceiver(this);
        }

        void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
        {

            entry.Logger.Information("\x1b[32m[[ModEntry.MultiplayerUI] Initializing MultiplayerUI...]\x1b[0m ");
            Hook_HUD.initHero += Hook_HUD_initking;
            Hook_Hero.updateLifeBar += Hook_Hero_kinglifupdate;
        }

        private void Hook_HUD_initking(Hook_HUD.orig_initHero orig, HUD self)
        {
            // The custom plate is owned by a vanilla FlowBox, so old HUD disposal owns its
            // render lifecycle. Drop our references before initializing the replacement HUD.
            try { ClearSlots(); } catch { }
            RemoveSpectateHint();

            orig(self);

            _hud = self;
            int slotCount = NetNode.MaxClientSlots;
            _slots = new LifeSlot?[slotCount];
            _slotActive = new bool[slotCount];

            // Remote HP is already cached during a level transition. Do not create font-backed
            // custom labels from inside Game.loadMainLevel; wait until the new HUD has settled.
            _hudReadyAfterTicks = System.Diagnostics.Stopwatch.GetTimestamp()
                + (long)(System.Diagnostics.Stopwatch.Frequency * 1.5);
        }
        public bool CanUseJumpHit()
        {
            try
            {
                var fastCheck = ModEntry.me?.cd?.fastCheck;
                if (fastCheck == null)
                    return false;

                int key = Cooldown.Encode(Cooldown.Keys.JUMP_HIT);
                return !fastCheck.exists(key);
            }
            catch { return false; }
        }
        public void Debugkeys()
        {
            // Developer spawn/perk/cooldown hotkeys are intentionally disabled in production.
            // They previously wrote directly into engine-owned fastCheck maps and constructed
            // runtime perk items during combat, both of which can poison Hashlink state.
        }
        private void Hook_Hero_kinglifupdate(Hook_Hero.orig_updateLifeBar orig, Hero self)
        {
            orig(self);
            KingLifeUpdate(self);
        }

        private dc.libs.Process process()
        {
            bool? titleLib = null;
            return new TitleScreen(titleLib);
        }

        public void KingLifeUpdate(Hero self)
        {
            // Driven from both Hook_Hero.updateLifeBar and OnHeroUpdate — in combat that is two
            // full passes per frame (snapshot lock, label Trim/allocations, percent formatting per
            // slot). One pass per rendered frame is enough; HP changes are picked up by the next
            // frame at worst.
            var nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (nowTicks - _lastKingLifeUpdateTicks < KingLifeUpdateMinIntervalTicks)
                return;
            _lastKingLifeUpdateTicks = nowTicks;

            _net = ModEntry._net;
            var net = _net;
            if (net == null)
            {
                if (MultiplayerSettingsStorage.DebugPartyHudPreview)
                {
                    RenderPartyPreview(self);
                    return;
                }

                if (_previewActive)
                    ClearSlots();

                HideSpectateHint();
                if (_boundNet != null)
                {
                    _boundNet = null;
                    lastLife = int.MinValue;
                    lastMaxLife = int.MinValue;
                    _lastHostConnectedClientCount = -1;
                    ClearSlots();
                }
                return;
            }

            if (!ReferenceEquals(_boundNet, net))
            {
                _boundNet = net;
                lastLife = int.MinValue;
                lastMaxLife = int.MinValue;
                _lastHostConnectedClientCount = -1;
                ClearSlots();
            }

            if (net.IsHost)
            {
                var connectedClients = NetNode.ConnectedClientCount;
                if (connectedClients != _lastHostConnectedClientCount)
                {
                    _lastHostConnectedClientCount = connectedClients;
                    lastLife = int.MinValue;
                    lastMaxLife = int.MinValue;
                }
            }
            else
            {
                _lastHostConnectedClientCount = -1;
            }


            if (lastLife != self.life || lastMaxLife != self.maxLife)
            {
                net.SendHP(self.life, self.maxLife, self.life, self.bonusLife, self.radius);
                lastLife = self.life;
                lastMaxLife = self.maxLife;
            }

            if (_slots.Length == 0)
            {
                HideSpectateHint();
                return;
            }

            if (!net.TryGetRemoteHpSnapshots(out var snapshots) || snapshots.Count == 0)
            {
                ClearSlots();
                HideSpectateHint();
                return;
            }

            var spectatedId = ModEntry.GetSpectatedRemoteId();
            string? spectatedName = null;
            var scale = CurrentPixelScale(_hud);
            try
            {
                System.Array.Clear(_slotActive, 0, _slotActive.Length);
                var localId = net.id;
                foreach (var remote in snapshots)
                {
                    if (!ModEntry.TryGetClientIndex(localId, remote.Id, out var slotIndex))
                        continue;
                    if (slotIndex < 0 || slotIndex >= _slots.Length)
                        continue;

                    // Position/name packets can create a remote snapshot before the first HP packet arrives.
                    // Do not render a broken 0 / 0 party frame; wait for real HP data.
                    if (remote.MaxLife <= 0)
                        continue;

                    var slot = _slots[slotIndex];
                    if (slot == null)
                    {
                        if (System.Diagnostics.Stopwatch.GetTimestamp() < _hudReadyAfterTicks)
                            continue;
                        var hud = _hud;
                        if (hud == null)
                            continue;
                        slot = CreatePlate(hud, slotIndex);
                        _slots[slotIndex] = slot;
                        Log.Information("[NetMod][PartyHUD] created plate slot={Slot} scale={Scale}", slotIndex, scale);
                    }

                    if (slot.Scale != scale)
                        RelayoutPlate(slot, scale);

                    var displayName = ModEntry.GetClientLabel(slotIndex);
                    if (string.IsNullOrWhiteSpace(displayName) ||
                        string.Equals(displayName, "Guest", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(displayName, LobbySession.RemoteUsername, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(remote.Username))
                            displayName = remote.Username.Trim();
                    }
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = "Guest";

                    var state = (PartyState)ModEntry.GetRemotePartyState(remote.Id);
                    var spectated = spectatedId > 0 && remote.Id == spectatedId;
                    if (spectated)
                        spectatedName = displayName;

                    UpdateSlotLabel(slot, displayName);
                    UpdateLifeBar(slot, remote.Life, remote.MaxLife, state, spectated);
                    _slotActive[slotIndex] = true;
                }
            }
            finally
            {
                NetNode.ReleaseConsumedList(snapshots);
            }

            RemoveInactiveSlots();
            UpdateSpectateHint(spectatedName, spectatedName == null ? 0 : ModEntry.CountSpectateTargets());
        }


        LifeSlot CreatePlate(HUD self, int slotIndex)
        {
            this.toplib = self.topRightFlowT;

            FlowBox owner = FlowBox.Class.createBoxValidation(
                null, Ref<double>.Null, Ref<double>.Null, Ref<bool>.Null, null);
            owner.isVertical = false;
            owner.box.alpha = 0;
            owner.set_horizontalAlign(new FlowAlign.Left());
            owner.set_verticalAlign(new FlowAlign.Top());
            owner.padBot = PlateGapY;

            var panel = new dc.h2d.Object(null);
            owner.addChild(panel);

            var chip = new Graphics(panel);
            chip.visible = true;

            var nameText = Assets.Class.makeText(
                "Guest".AsHaxeString(),
                dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                false,
                panel);
            nameText.textColor = ColorName;

            var statusText = Assets.Class.makeText(
                "0%".AsHaxeString(),
                dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                false,
                panel);
            statusText.textColor = ColorName;

            this.toplib.addChild(owner);
            this.toplib.isVertical = true;
            this.toplib.set_verticalAlign(new FlowAlign.Top());
            this.toplib.set_horizontalAlign(new FlowAlign.Right());

            return new LifeSlot(slotIndex, owner, panel)
            {
                ChipGraphics = chip,
                LabelText = nameText,
                StatusText = statusText,
                LastStatus = "0%"
            };
        }

        static double ArtToPx(double art, double scale) => System.Math.Floor(art * scale + 0.5);

        static double CurrentPixelScale(HUD? hud)
        {
            try
            {
                var scale = hud?.get_pixelScale.Invoke() ?? 1.0;
                return double.IsFinite(scale) && scale >= 1.0 ? scale : 1.0;
            }
            catch
            {
                return 1.0;
            }
        }

        static double MeasureTextWidth(dc.ui.Text? text, string value)
        {
            if (text == null || string.IsNullOrEmpty(value))
                return 0.0;
            try
            {
                return text.calcTextWidth(value.AsHaxeString()) * text.scaleX;
            }
            catch
            {
                return value.Length * 8.0;
            }
        }

        static double MeasureTextHeight(dc.ui.Text? text)
        {
            if (text == null)
                return 0.0;
            try
            {
                var height = text.textHeight * text.scaleY;
                return height > 0 ? height : 18.0;
            }
            catch
            {
                return 18.0;
            }
        }

        static string StatusLabel(PartyState state, int percent) => state switch
        {
            PartyState.Downed => DeadCellsMultiplayerMod.I18n.CoopText.T("Down"),
            PartyState.Out => DeadCellsMultiplayerMod.I18n.CoopText.T("Out"),
            _ => $"{percent}%"
        };

        static void RelayoutPlate(LifeSlot slot, double scale)
        {
            slot.Scale = scale;
            try { slot.LabelText?.onResize(); } catch { }
            try { slot.StatusText?.onResize(); } catch { }

            var rowPx = System.Math.Max(MeasureTextHeight(slot.LabelText), MeasureTextHeight(slot.StatusText));
            slot.RowHeightArt = System.Math.Ceiling(rowPx / scale);
            slot.RowTopArt = PlateEdge + PlatePadT;
            slot.BarTopArt = slot.RowTopArt + slot.RowHeightArt + PlateRowGap;
            slot.HeightArt = slot.BarTopArt + PlateBarH + PlatePadB + PlateEdge;

            var statusMax = System.Math.Max(
                MeasureTextWidth(slot.StatusText, "100%"),
                System.Math.Max(
                    MeasureTextWidth(slot.StatusText, StatusLabel(PartyState.Downed, 0)),
                    MeasureTextWidth(slot.StatusText, StatusLabel(PartyState.Out, 0))));
            slot.NameMaxWidthPx = System.Math.Max(
                0.0,
                ArtToPx(PlateContentR, scale) - ArtToPx(PlateContentL, scale) - statusMax - ArtToPx(PlateNameStatusGap, scale));

            try
            {
                slot.Container.set_minWidth((int)ArtToPx(PlateW, scale));
                slot.Container.set_minHeight((int)ArtToPx(slot.HeightArt, scale));
            }
            catch
            {
            }

            slot.LastLabel = null;
            slot.LastStatus = null;
            slot.LastLife = int.MinValue;
        }

        static string FitDisplayName(dc.ui.Text? text, string displayName, double maxWidthPx)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "Guest";
            displayName = displayName.Trim();
            if (text == null || MeasureTextWidth(text, displayName) <= maxWidthPx)
                return displayName;

            var ellipsis = MeasureTextWidth(text, "…") > 0 ? "…" : "...";
            for (var length = displayName.Length - 1; length > 0; length--)
            {
                if (char.IsHighSurrogate(displayName[length - 1]))
                    continue;
                var candidate = displayName[..length].TrimEnd() + ellipsis;
                if (MeasureTextWidth(text, candidate) <= maxWidthPx)
                    return candidate;
            }

            return ellipsis;
        }

        static void PlaceRowText(LifeSlot slot, dc.ui.Text? text, bool alignRight, string value)
        {
            if (text == null)
                return;

            var scale = slot.Scale;
            var rowTop = ArtToPx(slot.RowTopArt, scale);
            var rowHeight = ArtToPx(slot.RowHeightArt, scale);
            text.y = rowTop + System.Math.Floor((rowHeight - MeasureTextHeight(text)) * 0.5);
            text.x = alignRight
                ? ArtToPx(PlateContentR, scale) - System.Math.Ceiling(MeasureTextWidth(text, value))
                : ArtToPx(PlateContentL, scale);
        }

        static void UpdateSlotLabel(LifeSlot slot, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "Guest";
            if (slot.LabelText == null || slot.LastLabel == displayName)
                return;

            var fitted = FitDisplayName(slot.LabelText, displayName, slot.NameMaxWidthPx);
            slot.LabelText.set_text(fitted.AsHaxeString());
            PlaceRowText(slot, slot.LabelText, alignRight: false, fitted);
            slot.LastLabel = displayName;
        }

        static void UpdateLifeBar(LifeSlot slot, int life, int maxLife, PartyState state, bool spectated)
        {
            var safeMaxLife = System.Math.Max(1, maxLife);
            var safeLife = System.Math.Max(0, System.Math.Min(life, safeMaxLife));
            var percent = (int)System.Math.Round(safeLife * 100.0 / safeMaxLife);
            if (safeLife > 0 && percent == 0)
                percent = 1;
            var status = StatusLabel(state, percent);

            if (slot.LastLife == safeLife &&
                slot.LastMaxLife == safeMaxLife &&
                slot.LastState == state &&
                slot.LastSpectated == spectated &&
                string.Equals(slot.LastStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            DrawPlate(slot, safeLife, safeMaxLife, percent, state, spectated);

            if (slot.LabelText != null)
            {
                slot.LabelText.textColor = state == PartyState.Out
                    ? ColorTextMuted
                    : spectated ? ColorNameSpectated : ColorName;
            }

            if (slot.StatusText != null)
            {
                if (!string.Equals(slot.LastStatus, status, StringComparison.Ordinal))
                {
                    slot.StatusText.set_text(status.AsHaxeString());
                    PlaceRowText(slot, slot.StatusText, alignRight: true, status);
                }
                slot.StatusText.textColor = state switch
                {
                    PartyState.Downed => ColorDowned,
                    PartyState.Out => ColorTextMuted,
                    _ => percent <= 25 ? 0xFF9F8F : percent <= 50 ? 0xFFD98A : 0xBDF0B5
                };
            }

            slot.LastStatus = status;
            slot.LastLife = safeLife;
            slot.LastMaxLife = safeMaxLife;
            slot.LastState = state;
            slot.LastSpectated = spectated;
        }

        static void FillRect(Graphics g, double x0, double y0, double x1, double y1, int color, double alpha)
        {
            if (x1 <= x0 || y1 <= y0)
                return;
            g.beginFill(Ref<int>.From(ref color), Ref<double>.From(ref alpha));
            g.drawRect(x0, y0, x1 - x0, y1 - y0);
            g.endFill();
        }

        static void HpColors(int percent, out int fill, out int highlight, out int shade)
        {
            if (percent <= 25)
            {
                fill = 0xD9443F;
                highlight = 0xF08479;
                shade = 0x8E2626;
            }
            else if (percent <= 50)
            {
                fill = 0xE3A33A;
                highlight = 0xF7CF7A;
                shade = 0xA16C1F;
            }
            else
            {
                fill = 0x58C25A;
                highlight = 0x93E38C;
                shade = 0x34873C;
            }
        }

        static void DrawPlate(LifeSlot slot, int life, int maxLife, int percent, PartyState state, bool spectated)
        {
            var g = slot.ChipGraphics;
            if (g == null)
                return;

            try
            {
                g.clear();

                var scale = slot.Scale;
                var px = System.Math.Max(1.0, ArtToPx(1.0, scale));
                var w = ArtToPx(PlateW, scale);
                var h = ArtToPx(slot.HeightArt, scale);
                var edge = ArtToPx(1.0, scale);
                var inner = ArtToPx(PlateEdge, scale);

                HpColors(percent, out var hpFill, out var hpHighlight, out var hpShade);
                var accent = state switch
                {
                    PartyState.Downed => ColorDowned,
                    PartyState.Out => ColorOut,
                    _ => hpFill
                };

                FillRect(g, 0, 0, w, h, ColorEdge, 0.92);
                FillRect(g, edge, edge, w - edge, h - edge, spectated ? ColorFrameSpectated : ColorFrame, 1.0);
                FillRect(g, inner, inner, w - inner, h - inner, ColorPanel, 0.95);
                FillRect(g, inner, inner, w - inner, inner + px, ColorPanelLight, 1.0);
                FillRect(g, inner, inner, inner + ArtToPx(PlateAccentW, scale), h - inner, accent, 1.0);

                var barX0 = ArtToPx(PlateContentL, scale);
                var barX1 = ArtToPx(PlateContentR, scale);
                var barY0 = ArtToPx(slot.BarTopArt, scale);
                var barY1 = ArtToPx(slot.BarTopArt + PlateBarH, scale);
                var barBack = state switch
                {
                    PartyState.Downed => ColorBarBackDowned,
                    PartyState.Out => ColorBarBackOut,
                    _ => ColorBarBack
                };

                FillRect(g, barX0 - px, barY0 - px, barX1 + px, barY1 + px, ColorEdge, 1.0);
                FillRect(g, barX0, barY0, barX1, barY1, barBack, 1.0);

                if (state == PartyState.Alive && life > 0)
                {
                    var fillPx = System.Math.Max(1.0, System.Math.Round((barX1 - barX0) * life / (double)maxLife));
                    var fillX1 = barX0 + fillPx;
                    FillRect(g, barX0, barY0, fillX1, barY1, hpFill, 1.0);
                    FillRect(g, barX0, barY0, fillX1, barY0 + px, hpHighlight, 1.0);
                    FillRect(g, barX0, barY1 - px, fillX1, barY1, hpShade, 1.0);
                }

                for (var i = 1; i < PlateBarTicks; i++)
                {
                    var tickX = barX0 + System.Math.Round((barX1 - barX0) * i / PlateBarTicks) - System.Math.Floor(px * 0.5);
                    FillRect(g, tickX, barY0, tickX + px, barY1, ColorEdge, 0.45);
                }
            }
            catch
            {
            }
        }

        void UpdateSpectateHint(string? targetName, int targetCount)
        {
            var hud = _hud ?? HUD.Class.ME;
            var root = hud?.root;
            if (root == null ||
                string.IsNullOrWhiteSpace(targetName) ||
                System.Diagnostics.Stopwatch.GetTimestamp() < _hudReadyAfterTicks)
            {
                HideSpectateHint();
                return;
            }

            try
            {
                if (_spectateHintTitle == null || _spectateHintTitle.parent == null || !ReferenceEquals(_spectateHintTitle.parent, root))
                {
                    RemoveSpectateHint();
                    _spectateHintTitle = Assets.Class.makeText(
                        " ".AsHaxeString(),
                        dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                        false,
                        root);
                    _spectateHintTitle.textColor = ColorSpectateHint;
                    _spectateHintSub = Assets.Class.makeText(
                        " ".AsHaxeString(),
                        dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                        false,
                        root);
                    _spectateHintSub.textColor = ColorSpectateHintSub;
                }

                var title = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    DeadCellsMultiplayerMod.I18n.CoopText.T("Spectating {0}"),
                    targetName.Trim());
                var sub = targetCount > 1
                    ? DeadCellsMultiplayerMod.I18n.CoopText.T("Switch player: , / .")
                    : string.Empty;

                if (!string.Equals(_spectateHintTitleValue, title, StringComparison.Ordinal))
                {
                    _spectateHintTitle.set_text(title.AsHaxeString());
                    _spectateHintTitleValue = title;
                }
                if (_spectateHintSub != null && !string.Equals(_spectateHintSubValue, sub, StringComparison.Ordinal))
                {
                    _spectateHintSub.set_text((sub.Length == 0 ? " " : sub).AsHaxeString());
                    _spectateHintSubValue = sub;
                }

                var scale = CurrentPixelScale(hud);
                double stageW = dc.libs.Process.Class.CUSTOM_STAGE_WIDTH;
                double stageH = dc.libs.Process.Class.CUSTOM_STAGE_HEIGHT;
                if (stageW <= 0 || stageH <= 0)
                {
                    var win = dc.hxd.Window.Class.getInstance();
                    stageW = win?.get_width() ?? 0;
                    stageH = win?.get_height() ?? 0;
                }

                var bottom = stageH - ArtToPx(SpectateHintBottomArt, scale);
                var subHeight = sub.Length == 0 ? 0.0 : MeasureTextHeight(_spectateHintSub);
                var titleHeight = MeasureTextHeight(_spectateHintTitle);

                _spectateHintTitle.x = System.Math.Floor((stageW - MeasureTextWidth(_spectateHintTitle, title)) * 0.5);
                _spectateHintTitle.y = System.Math.Floor(bottom - subHeight - titleHeight);
                _spectateHintTitle.visible = true;

                if (_spectateHintSub != null)
                {
                    _spectateHintSub.x = System.Math.Floor((stageW - MeasureTextWidth(_spectateHintSub, sub)) * 0.5);
                    _spectateHintSub.y = System.Math.Floor(bottom - subHeight);
                    _spectateHintSub.visible = subHeight > 0;
                }
            }
            catch
            {
                HideSpectateHint();
            }
        }

        void RenderPartyPreview(Hero self)
        {
            var hud = _hud;
            if (hud == null || _slots.Length == 0)
                return;
            if (System.Diagnostics.Stopwatch.GetTimestamp() < _hudReadyAfterTicks)
                return;

            _previewActive = true;
            var scale = CurrentPixelScale(hud);
            var maxLife = System.Math.Max(1, self.maxLife);
            var rows = new (string Name, int Life, PartyState State, bool Spectated)[]
            {
                ("Chifer123536", self.life, PartyState.Alive, true),
                ("A_really_long_steam_nickname_here", maxLife * 2 / 5, PartyState.Alive, false),
                ("Беатрис", maxLife / 6, PartyState.Downed, false),
                ("Guest", 0, PartyState.Out, false)
            };

            var count = System.Math.Min(rows.Length, _slots.Length);
            for (var i = 0; i < count; i++)
            {
                var slot = _slots[i] ??= CreatePlate(hud, i);
                if (slot.Scale != scale)
                    RelayoutPlate(slot, scale);
                UpdateSlotLabel(slot, rows[i].Name);
                UpdateLifeBar(slot, rows[i].Life, maxLife, rows[i].State, rows[i].Spectated);
            }

            UpdateSpectateHint(rows[0].Name, 2);
        }

        void HideSpectateHint()
        {
            try
            {
                if (_spectateHintTitle != null)
                    _spectateHintTitle.visible = false;
                if (_spectateHintSub != null)
                    _spectateHintSub.visible = false;
            }
            catch
            {
            }
        }

        void RemoveSpectateHint()
        {
            try { _spectateHintTitle?.remove(); } catch { }
            try { _spectateHintSub?.remove(); } catch { }
            _spectateHintTitle = null;
            _spectateHintSub = null;
            _spectateHintTitleValue = null;
            _spectateHintSubValue = null;
        }

        private void ClearSlots()
        {
            _previewActive = false;
            if (_slots.Length == 0)
                return;

            var removed = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null)
                    continue;
                try
                {
                    toplib?.removeChild(slot.Container);
                    slot.Container.remove();
                    removed++;
                }
                catch { }
                _slots[i] = null;
            }

            if (removed > 0)
                Log.Information("[NetMod][PartyHUD] cleared {Count} plate(s)", removed);
        }

        private void RemoveInactiveSlots()
        {
            if (_slots.Length == 0)
                return;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slotActive[i])
                    continue;
                var slot = _slots[i];
                if (slot == null)
                    continue;
                try
                {
                    toplib?.removeChild(slot.Container);
                    slot.Container.remove();
                }
                catch { }
                _slots[i] = null;
            }
        }


        private sealed class SystemMessageEntry
        {
            public dc.h2d.Text Text = null!;
            public double LifetimeSeconds;
            public double FadeSeconds;
            public double ElapsedSeconds;
        }

        private sealed class PendingSystemMessage
        {
            public string Text = string.Empty;
            public double LifetimeSeconds;
            public double FadeSeconds;
        }

        private static readonly object SystemMsgSync = new();
        private static readonly Queue<PendingSystemMessage> PendingSystemMessages = new();
        private static readonly List<SystemMessageEntry> ActiveSystemMessages = new();

        private const int MaxSystemMessages = 8;
        private const double DefaultSystemMsgLifetimeSeconds = 10.0;
        private const double DefaultSystemMsgFadeSeconds = 2.5;
        private const double SystemMsgXOffsetPx = 20.0;
        private const double SystemMsgYOffsetPx = 250.0;
        private const double SystemMsgScale = 1.2;

        public static void PushSystemMessage(string message, double lifetimeSeconds = DefaultSystemMsgLifetimeSeconds, double fadeSeconds = DefaultSystemMsgFadeSeconds)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var normalizedLifetime = System.Math.Max(0.25, lifetimeSeconds);
            var normalizedFade = System.Math.Max(0.15, System.Math.Min(fadeSeconds, normalizedLifetime));
            lock (SystemMsgSync)
            {
                PendingSystemMessages.Enqueue(new PendingSystemMessage
                {
                    Text = message.Trim(),
                    LifetimeSeconds = normalizedLifetime,
                    FadeSeconds = normalizedFade
                });
            }
        }

        public void DebugUI(string @string)
        {
            PushSystemMessage(@string, 5.0, 1.5);
        }

        private static bool EnsureSystemMessageFlow()
        {
            var hud = HUD.Class.ME;
            var root = hud?.root;
            if (root == null)
                return false;
            if (hud == null)
                return false;

            if (flowContainer == null || flowContainer.parent == null || !ReferenceEquals(flowContainer.parent, root))
            {
                try { flowContainer?.remove(); } catch { }
                flowContainer = new dc.h2d.Flow(root);
                flowContainer.multiline = true;
                flowContainer.isVertical = true;
                flowContainer.set_verticalAlign(new FlowAlign.Top());
                flowContainer.set_horizontalAlign(new FlowAlign.Left());
            }

            var pixelScale = hud.get_pixelScale.Invoke();
            flowContainer.x = SystemMsgXOffsetPx * pixelScale;
            flowContainer.y = SystemMsgYOffsetPx * pixelScale;
            flowContainer.alpha = 1;
            flowContainer.visible = true;
            root.addChild(flowContainer);
            return true;
        }

        private static void RemoveSystemMessageAt(int index)
        {
            if (index < 0 || index >= ActiveSystemMessages.Count)
                return;

            var entry = ActiveSystemMessages[index];
            try
            {
                flowContainer?.removeChild(entry.Text);
                entry.Text.remove();
            }
            catch
            {
            }
            ActiveSystemMessages.RemoveAt(index);
        }

        private static void EnqueueSystemMessageInternal(PendingSystemMessage pending)
        {
            if (flowContainer == null || pending == null)
                return;

            var text = Assets.Class.makeText(
                pending.Text.AsHaxeString(),
                dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()),
                false,
                flowContainer);
            text.customScale = SystemMsgScale;
            text.onResize();
            text.textColor = 0xFFFFFF;
            text.alpha = 1;

            ActiveSystemMessages.Add(new SystemMessageEntry
            {
                Text = text,
                LifetimeSeconds = pending.LifetimeSeconds,
                FadeSeconds = pending.FadeSeconds,
                ElapsedSeconds = 0
            });

            while (ActiveSystemMessages.Count > MaxSystemMessages)
                RemoveSystemMessageAt(0);
        }

        private void UpdateSystemMessages(double dt)
        {
            if (!EnsureSystemMessageFlow())
                return;

            lock (SystemMsgSync)
            {
                while (PendingSystemMessages.Count > 0)
                    EnqueueSystemMessageInternal(PendingSystemMessages.Dequeue());
            }

            if (ActiveSystemMessages.Count == 0)
                return;

            for (int i = ActiveSystemMessages.Count - 1; i >= 0; i--)
            {
                var msg = ActiveSystemMessages[i];
                msg.ElapsedSeconds += dt;

                var fadeStart = System.Math.Max(0.0, msg.LifetimeSeconds - msg.FadeSeconds);
                if (msg.ElapsedSeconds >= fadeStart)
                {
                    var fadeT = (msg.ElapsedSeconds - fadeStart) / System.Math.Max(0.01, msg.FadeSeconds);
                    var alpha = 1.0 - fadeT;
                    if (alpha < 0) alpha = 0;
                    msg.Text.alpha = alpha;
                }

                if (msg.ElapsedSeconds >= msg.LifetimeSeconds)
                    RemoveSystemMessageAt(i);
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            UpdateSystemMessages(dt);
            var hero = ModEntry.me;
            if (hero != null)
                KingLifeUpdate(hero);
            Debugkeys();
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  PARTY HUD — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Плашка на напарника в HUD.topRightFlowT (вертикально, по правому краю), отступ снизу PlateGapY.
 *  Геометрия в арт-пикселях × HUD pixelScale (1080p = 3), края округляются до целых px.
 *  Текст dc.ui.Text в родном масштабе игры; высота строки и ширины меряются (textHeight,
 *  calcTextWidth), ник обрезается по реальной ширине с «…». Смена pixelScale → RelayoutPlate.
 *  Статус: % HP / «Упал» / «Выбыл» (ModEntry.GetRemotePartyState). Цель наблюдения — золотая рамка.
 *  Подсказка «Наблюдение: ник» внизу по центру, пока свой игрок лежит и камера на напарнике.
 *  DebugPartyHudPreview: без сети рисует 4 тестовые плашки поверх соло-забега.
 * ═══════════════════════════════════════
*/
