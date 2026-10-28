namespace DeadCellsMultiplayerMod;

/// <summary>
/// One authoritative identity for packaging, log diagnostics, and the network handshake.
/// Bump <see cref="NetworkProtocolVersion"/> whenever peers would interpret a packet differently.
/// </summary>
internal static class BuildInfo
{
    public const string Version = "0.8.91-coop.6";
    public const string SourceMarker = "v0.8.91-coop.6-p25";
    // Protocol 19: launch convergence, level-boundary identity, sequenced level seeds and ordered player positions.
    //   * The host re-publishes the full launch prerequisite set (GEN/CGDATA/RUNCOMMIT/SEED/
    //     RUNEXEC) until the client confirms, and GEN is host-cached and replayed to late joiners.
    //     A protocol 17 peer never sends or expects that repetition.
    //   * RUNREADY is now actually emitted, so the session phase reaches Playing and the save guard
    //     disarms. A peer that never sends it would leave the other side's launch unconfirmed.
    //   * EXITREADY carries the reporting peer's level id; a protocol 17 peer omits it and its
    //     readiness would be accepted for the wrong level.
    //   * EXITCOMMIT: the host now AUTHORS level transitions (sequence + door + destination). A
    //     peer that neither sends nor waits for it would transition on its own timing.
    //   * SPAWNANCHOR: host-approved safe spawn for mid-run joiners.
    //   * Boss snapshots may carry Conjunctivius vulnerability/arena-platform fields; a peer
    //     without them computes vulnerability locally and can disagree with the host.
    //   * LSEED now carries a monotonic generation sequence (level|seq|seed), preventing a delayed
    //     seed from a previous visit/restart from being consumed by a new generation of the same
    //     level id. Protocol 18 interpreted the second field as the seed itself.
    //   * Position snapshots carry the sender level id plus a monotonic sequence, so a delayed
    //     unreliable Steam packet from the previous passage cannot overwrite new-world coordinates.
    // Protocol 20 (coop fork):
    //   * Position snapshots carry the sender's monotonic timestamp; remote players are rendered
    //     through a jitter-adaptive interpolation buffer and ANIM/HEADANIM/ATK follow its clock.
    //   * DLCOWN / DLCSET negotiate the session DLC set; RunLaunchDescriptor.DlcFlags carries it.
    // Protocol 21 (coop fork):
    //   * BALANCE carries the host co-op difficulty and the mob damage multiplier applied to heroes.
    // Protocol 22 (coop fork):
    //   * SCROLLCFG / SCROLLOPEN / SCROLLSEL / SCROLLEND pause every peer while someone picks a
    //     scroll and mirror the picker's cursor. A peer without them never pauses or resumes.
    //   * EMOTE plays an emote on the sender's ghost; the emote's body anims are not sent as ANIM.
    //   * ACH shares a Steam achievement the sender's game unlocked. Additive: a protocol 22 peer
    //     without it drops the line, nothing else is interpreted differently.
    // Protocol 23 (coop fork):
    //   * The client hit report (hit|life|damage) carries the damage its replica actually took
    //     (AttackData.finalDmg); the host applies exactly that. Protocol 22 sent the pre-scaling
    //     base damage and the host rescaled it with its own hero.
    // Protocol 24 (coop fork):
    //   * PDOWN state 2 = dead until the next passage (one revive per level); protocol 23 rejects it.
    //   * RVHOLD / RVDONE share revive progress and confirm a revive; the reviver spends the flask
    //     only on RVDONE.
    // Protocol 25 (coop fork):
    //   * MOBMOVE / MOBSTATE Time is the host's monotonic clock in ms (was the level frame time);
    //     clients buffer mob positions on that clock. Protocol 24 reads it as frames.
    // These change how peers interpret the same exchange, so cross-version pairing is rejected.
    public const int NetworkProtocolVersion = 25;
}
