using dc.en;
using dc.pr;
using ModCore.Utilities;
using Serilog;
using dc;
using HaxeProxy.Runtime;
using dc.shader;
using dc.hl.types;
using Hashlink.Virtuals;
using dc.libs.heaps.slib;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using DeadCellsMultiplayerMod.Core.Abstractions;
using DeadCellsMultiplayerMod.Core.Services;
using DeadCellsMultiplayerMod.Ghost.Rendering;

namespace DeadCellsMultiplayerMod
{
    public class GhostHero
    {
        /// <summary>
        /// Shared label renderer for all ghost entities. Labels are keyed by entity,
        /// so one instance serves every GhostHero.
        /// </summary>
        private static readonly IGhostRenderer _labelManager =
            new GhostLabelManager(new DisplayScaleProvider());

        private readonly Hero _me;
        private static ILogger? _log;

        public int PlayerId { get; }

        public GhostKing king = null!;
        public KingHead.Kinghead kinghead = null!;


        public GhostHero(
        int playerId,
        dc.pr.Game game,
        Hero me,
        ILogger logger,
        ModEntry entry)
        {
            PlayerId = playerId;
            _ = game;
            _ = entry;
            _me = me;
            _log = logger;
        }


        public GhostKing CreateGhostKing(Level level, string? label = null)
        {

            king = new GhostKing(level, (int)-1000, (int)-1000);
            king.init();
            king.set_level(level);
            king.set_team(level.teamHero);
            king._targetable = true;
            king.hasWineGlass = false;
            king.lifeBarAbove = true;
            king.initLife(100, 100);
            king.hasRepelling = true;
            king.collisionMode = new CollisionMode.Normal();
            king.hasEntityTouchChecks = true;
            king.onActivate(_me, true);
            king.canBeActivated(_me);
            king.needsLongPress = true;
            king.hasEntityTouchChecks = true;


            bool sics = false;
            king.enableAllPhysics(Ref<bool>.From(ref sics));
            king.visible = true;
            var miniMap = ModEntry.miniMap;
            if (miniMap != null && _me._level == king._level)
            {
                miniMap.track(king, 14888237, "minimapHero".AsHaxeString(), null, true, null, null, null);
            }
            if (!string.IsNullOrWhiteSpace(label))
                SetLabel(king, label);
            king.spr._animManager.play("idle".AsHaxeString(), null, null).loop(null);
            return king;
        }

        public void disposeKing(GhostKing k)
        {
            if (k == null)
                return;

            if (ReferenceEquals(king, k))
                king = null!;

            // Лейблы теперь управляются GhostLabelManager автоматически

            DisposeKingRuntime(k);
        }

        public static void DisposeKingRuntime(GhostKing k)
        {
            if (k == null)
                return;

            List<Exception>? failures = null;
            try
            {
                Level? level = null;
                try { level = k._level; } catch { }

                try
                {
                    if (k.spr != null)
                    {
                        ColorMap shader = (ColorMap)k.spr.getShader(ColorMap.Class);
                        if (shader != null)
                        {
                            k.spr.removeShader(shader);
                            k.spr.lib = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures ??= new List<Exception>();
                    failures.Add(ex);
                }

                try
                {
                    if (!k.destroyed)
                        k.destroy();
                }
                catch (Exception ex)
                {
                    failures ??= new List<Exception>();
                    failures.Add(ex);
                }

                if (level != null)
                {
                    try { level.runEntitiesGC(); } catch { }
                    try { RemoveKingFromLevelCollections(level, k); } catch { }
                }
                else
                {
                    try { k.dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(ex);
            }

            if (failures != null && failures.Count > 0)
                _log?.Warning("[NetMod] GhostKing dispose reported {Count} step failure(s)", failures.Count);
        }

        private static void RemoveKingFromLevelCollections(Level level, GhostKing k)
        {
            level.entities?.remove(k);
            level.qTreeEntities?.remove(k);
            level.savedEntities?.remove(k);
            level.entitiesGC?.remove(k);

            ArrayBytes_Int? clids = null;
            try { clids = k.getEntityCLIDS(); } catch { }
            if (clids == null || level.entitiesByClass == null)
                return;

            for (var i = 0; i < clids.length; i++)
            {
                var entries = level.entitiesByClass.get(clids.getDyn(i)) as ArrayObj;
                entries?.remove(k);
            }
        }

        /// <summary>
        /// Strip any GhostKing instances from a level's entity collections so they cannot leak into
        /// MSave / Continue. Safe to call during level create and immediately before writeSave.
        /// </summary>
        public static int PurgeGhostKingsFromLevel(Level? level)
        {
            if (level == null)
                return 0;

            var ghosts = new HashSet<GhostKing>();
            CollectGhostKings(level.entities, ghosts);
            CollectGhostKings(level.qTreeEntities, ghosts);
            CollectGhostKings(level.savedEntities, ghosts);
            CollectGhostKings(level.entitiesGC, ghosts);

            foreach (var ghost in ghosts)
                DisposeKingRuntime(ghost);

            return ghosts.Count;
        }

        public static int PurgeGhostKingsFromCurrentGame()
        {
            try
            {
                Level? level = null;
                try { level = ModEntry.me?._level; } catch { }
                if (level == null)
                {
                    try { level = ModEntry.Instance?.game?.curLevel; } catch { }
                }

                return PurgeGhostKingsFromLevel(level);
            }
            catch
            {
                return 0;
            }
        }

        private static void CollectGhostKings(ArrayObj? entries, HashSet<GhostKing> ghosts)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.length; i++)
            {
                if (entries.getDyn(i) is GhostKing ghost)
                    ghosts.Add(ghost);
            }
        }

        public void SetLabel(Entity entity, string? text)
        {
            // Делегируем новой системе рендеринга
            _labelManager.SetLabel(entity, text);
        }

        public void UpdateLabels()
        {
            // Делегируем новой системе рендеринга
            _labelManager.UpdateLabels();
        }

    }
}
