using System.Diagnostics;
using System.Runtime.InteropServices;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    const int MaxGatedAnimEvents = 8;
    const int MaxGatedAttackEvents = 32;
    const double MaxGatedEventHoldMs = 300.0;

    readonly record struct GatedAnim(double Stamp, double ArrivalMs, string? Anim, int? Queue, bool? G);
    readonly record struct GatedHeadAnim(double Stamp, double ArrivalMs, string Anim);
    readonly record struct GatedAttack(double Stamp, double ArrivalMs, RemoteAttack Attack);

    sealed class RemoteMotionState
    {
        public readonly RemoteMotionBuffer Buffer = new();
        public readonly Queue<GatedAnim> Anims = new();
        public readonly Queue<GatedHeadAnim> HeadAnims = new();
        public readonly Queue<GatedAttack> Attacks = new();
    }

    [ThreadStatic] static double _dispatchArrivalMs;

    internal static double MonotonicMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    static double ArrivalMs() => _dispatchArrivalMs > 0.0 ? _dispatchArrivalMs : MonotonicMs();

    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    static extern uint timeEndPeriod(uint period);

    static bool TryRaiseTimerResolution()
    {
        try { return timeBeginPeriod(1) == 0; } catch { return false; }
    }

    static void RestoreTimerResolution(bool raised)
    {
        if (!raised)
            return;
        try { timeEndPeriod(1); } catch { }
    }

    void PushRemoteMotionLocked(RemotePlayerState state, double senderMs, double x, double y, int dir)
    {
        var arrival = ArrivalMs();
        state.Motion.Buffer.Push(senderMs > 0.0 ? senderMs : arrival, arrival, x, y, dir);
    }

    bool TrySampleRemoteMotionLocked(RemotePlayerState state, double nowMs, out double x, out double y, out int dir)
    {
        return state.Motion.Buffer.TrySample(nowMs, out x, out y, out dir);
    }

    void GateRemoteAnimLocked(RemotePlayerState state, string? anim, int? queue, bool? g)
    {
        var now = ArrivalMs();
        var anims = state.Motion.Anims;
        while (anims.Count >= MaxGatedAnimEvents)
            anims.Dequeue();
        anims.Enqueue(new GatedAnim(state.Motion.Buffer.StampForEvent(now), now, anim, queue, g));
    }

    void GateRemoteHeadAnimLocked(RemotePlayerState state, string anim)
    {
        var now = ArrivalMs();
        var heads = state.Motion.HeadAnims;
        while (heads.Count >= MaxGatedAnimEvents)
            heads.Dequeue();
        heads.Enqueue(new GatedHeadAnim(state.Motion.Buffer.StampForEvent(now), now, anim));
    }

    void GateRemoteAttackLocked(RemotePlayerState state, RemoteAttack attack)
    {
        var now = ArrivalMs();
        var attacks = state.Motion.Attacks;
        while (attacks.Count >= MaxGatedAttackEvents)
            AddBoundedLocked(_pendingAttacks, attacks.Dequeue().Attack, PendingAttackLimit);
        attacks.Enqueue(new GatedAttack(state.Motion.Buffer.StampForEvent(now), now, attack));
    }

    bool IsGatedEventDue(RemotePlayerState state, double stamp, double arrivalMs, double nowMs, bool force)
    {
        return force ||
               nowMs - arrivalMs >= MaxGatedEventHoldMs ||
               state.Motion.Buffer.IsDue(stamp);
    }

    void ReleaseDueRemoteVisualsLocked(RemotePlayerState state, double nowMs, bool force = false)
    {
        var anims = state.Motion.Anims;
        while (anims.Count > 0)
        {
            var next = anims.Peek();
            if (!IsGatedEventDue(state, next.Stamp, next.ArrivalMs, nowMs, force))
                break;
            anims.Dequeue();
            state.Anim = next.Anim;
            state.AnimQueue = next.Queue;
            state.AnimG = next.G;
            state.HasAnim = true;
        }

        var heads = state.Motion.HeadAnims;
        while (heads.Count > 0)
        {
            var next = heads.Peek();
            if (!IsGatedEventDue(state, next.Stamp, next.ArrivalMs, nowMs, force))
                break;
            heads.Dequeue();
            state.HeadAnim = next.Anim;
            state.HasHeadAnim = true;
        }
    }

    void ReleaseDueRemoteAttacksLocked(double nowMs, bool force = false)
    {
        foreach (var state in _remotes.Values)
        {
            var attacks = state.Motion.Attacks;
            while (attacks.Count > 0)
            {
                var next = attacks.Peek();
                if (!IsGatedEventDue(state, next.Stamp, next.ArrivalMs, nowMs, force))
                    break;
                attacks.Dequeue();
                AddBoundedLocked(_pendingAttacks, next.Attack, PendingAttackLimit);
            }
        }
    }

    void ResetRemoteMotionLocked(RemotePlayerState state)
    {
        var now = MonotonicMs();
        ReleaseDueRemoteVisualsLocked(state, now, force: true);
        var attacks = state.Motion.Attacks;
        while (attacks.Count > 0)
            AddBoundedLocked(_pendingAttacks, attacks.Dequeue().Attack, PendingAttackLimit);
        state.Motion.Buffer.Reset();
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE MOTION — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Позиции удалённого игрока → RemoteMotionBuffer (интерполяция с адаптивной задержкой).
 *  ANIM / HEADANIM / ATK не применяются сразу: встают в очередь со штампом времени отправителя
 *  и выпускаются, когда часы рендера буфера до него доходят — анимации и удары совпадают с позицией.
 *  Время прихода берётся в сетевом потоке при чтении пакета (_dispatchArrivalMs), а не при обработке
 *  на главном потоке — кадр игры не добавляет джиттер.
 *  На время Steam-сессии таймер Windows 1 мс (timeBeginPeriod) — опрос приёма каждые ~8 мс, а не 15.6.
 *  Страховка: событие держится не дольше 300 мс; сброс буфера (смена уровня) выпускает всё сразу.
 *  Все методы *Locked вызываются под _sync.
 * ═══════════════════════════════════════
*/
