namespace DeadCellsMultiplayerMod.Core.Constants;

/// <summary>
/// Core game-related constants used throughout the mod.
/// </summary>
public static class GameConstants
{
    // Display modes
    public const int WindowedDisplayMode = 0;
    public const int FullscreenDisplayMode = 1;
    public const int BorderlessDisplayMode = 2;

    // Nickname scaling
    public const double NicknameScaleWindowed = 0.8;
    public const double NicknameScaleFullscreen = 0.5;

    // Default skins
    public const string DefaultBodySkin = "PrisonerDefault";
    public const string DefaultHeadSkin = "BaseFlame";

    // Revival
    public const double ReviveUseDistancePx = 48.0;
    public const double ReviveAttemptCooldownSeconds = 0.2;
    public const double ReviveHoldSeconds = 0.7;
    public const double ReviveHomunculusBodyMaxDistancePx = 64.0;
    public const double ReviveRemotePositionValidationPx = 96.0;
    public const string ReviveHintText = "Hold to revive.";

    // Downed state
    public const double RemoteDownedStateStaleSeconds = 6.0;
    public const double DownedStateResendSeconds = 0.4;
    public const double DownedHeadStateResendSeconds = 1.0 / 30.0;
    public const double DownedGhostBodyYOffsetPx = 40.0;
    public const double LocalReviveBodyYOffsetPx = 0.5;
    public const double PostRevivePositionLockSeconds = 0.0;

    // Client disposal
    public const double ClientDisposeTransitionSeconds = 0.28;
    public const double GhostHeadDormantUpdateSeconds = 0.20;
    public const double GhostHeadRecreateRetrySeconds = 0.25;

    // Boss cinematics
    public const double BossCineSendCooldownSeconds = 2.0;
    public const double BossCineApplyPendingTtlSeconds = 20.0;
    public const double BossCineEchoSuppressSeconds = 12.0;
    public const double BossHeroTeleportYOffsetPx = 20.0;
    public const double BossHeroTeleportEchoSuppressSeconds = 1.5;

    // Dive attack
    public const double RemoteDiveReplayMinSeconds = 0.08;
    public const int DiveAttackCooldownKey = 729808896;
    public const int HeroControlLockCooldownKey = 255852544;
    public const int HeroSkillLockCooldownKey = 174063616;
    public const int HeroDiveRuntimeCooldownKey = 719323136;

    // Network
    public const int SteamP2PChannelClientToHost = 0;
    public const int SteamP2PChannelHostToClient = 1;
    public const uint SteamMaxPacketSizeBytes = 16u * 1024u * 1024u;
    public const int SteamMinReceiveBufferBytes = 64 * 1024;
    public const double SteamReceiveTimeoutSeconds = 30.0;
    public const double SteamKeepAliveSeconds = 3.0;
    public const int SteamKeepAlivePollMs = 500;
    public const int SteamOverlayCallbackMaxRetries = 600;
    public const int SteamOverlayLaunchPollIntervalMs = 500;
    public const int SteamOverlayJoinDedupMs = 3000;

    // Misc
    public const int MaxCoopIdLength = 128;
    public const string ExplorerRunePermanentItemId = "ExploKey";
    public const int MaxDebugExplorerRevealAllCalls = 3;
    public const int MaxCachedHostLevelPayloads = 12;
}
