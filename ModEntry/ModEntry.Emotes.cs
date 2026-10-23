using DeadCellsMultiplayerMod.Ghost.GhostBase;

namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry
    {
        internal static bool IsHeroAttackAnim(string anim) => IsAttackAnim(anim);

        internal static bool TryGetGhost(int remoteId, out GhostKing ghost)
        {
            ghost = null!;
            var localId = _net?.id ?? 0;
            if (!TryGetClientIndex(localId, remoteId, out var index))
                return false;

            var client = clients[index];
            if (client == null || client.destroyed || client.spr == null)
                return false;

            ghost = client;
            return true;
        }

        internal static void RestoreGhostBodyAnim(int remoteId)
        {
            var instance = Instance;
            var localId = _net?.id ?? 0;
            if (instance == null || !TryGetClientIndex(localId, remoteId, out var index))
                return;

            var client = clients[index];
            if (client == null || client.destroyed)
                return;

            instance.PlayGhostAnim(
                client,
                clientLastBodyAnims[index] ?? "idle",
                clientLastBodyAnimQueues[index],
                clientLastBodyAnimGs[index]);
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  MODENTRY EMOTES — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Мост для EmoteSync к приватному состоянию призраков:
 *  TryGetGhost — живой GhostKing игрока по его сетевому id (тот же слот, что у ANIM).
 *  RestoreGhostBodyAnim — вернуть призраку последнюю полученную ANIM (или idle) после эмоции.
 * ═══════════════════════════════════════
*/
