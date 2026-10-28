using System.Threading;

namespace DeadCellsMultiplayerMod.PortableCore;

internal static class MobPuppetStats
{
    static long s_poses;
    static long s_extrapolated;
    static long s_groundSnaps;
    static long s_gatedAttacks;
    static long s_lateAttacks;

    public static void RecordPose(bool extrapolated)
    {
        Interlocked.Increment(ref s_poses);
        if (extrapolated)
            Interlocked.Increment(ref s_extrapolated);
    }

    public static void RecordGroundSnap() => Interlocked.Increment(ref s_groundSnaps);

    public static void RecordGatedAttack() => Interlocked.Increment(ref s_gatedAttacks);

    public static void RecordLateAttack() => Interlocked.Increment(ref s_lateAttacks);

    public static bool TryDrain(out long poses, out long extrapolated, out long groundSnaps, out long gatedAttacks, out long lateAttacks)
    {
        poses = Interlocked.Exchange(ref s_poses, 0);
        extrapolated = Interlocked.Exchange(ref s_extrapolated, 0);
        groundSnaps = Interlocked.Exchange(ref s_groundSnaps, 0);
        gatedAttacks = Interlocked.Exchange(ref s_gatedAttacks, 0);
        lateAttacks = Interlocked.Exchange(ref s_lateAttacks, 0);
        return poses > 0 || gatedAttacks > 0 || lateAttacks > 0;
    }
}

/*
 * ═══════════════════════════════════════
 *  MOB PUPPET STATS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Счётчики кукол мобов за окно [NetHealth] (Interlocked, сброс при чтении):
 *  позы из буфера, из них экстраполяция; правки Y на земле (> 6 px);
 *  атаки, придержанные до времени хоста; атаки, выпущенные по таймауту 300 мс (буфер не дошёл).
 * ═══════════════════════════════════════
*/
