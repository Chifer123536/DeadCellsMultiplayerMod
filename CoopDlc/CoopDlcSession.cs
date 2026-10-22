using DeadCellsMultiplayerMod.PortableCore;
using Steamworks;

namespace DeadCellsMultiplayerMod;

internal static class CoopDlcSession
{
    public const ulong DescriptorPresentFlag = 0x100;

    static int _realMask = -1;
    static int _appliedMask = -1;
    static int _sessionMask = -1;
    static int _sessionRevision;
    static int _hostOwnedMask = -1;

    public static int SessionMask => _sessionMask;
    public static int HostOwnedMask => _hostOwnedMask;
    public static bool IsOverriding => _appliedMask >= 0;

    public static int RealMask => EnsureRealMask();

    public static ulong DescriptorFlags =>
        _sessionMask >= 0 ? DescriptorPresentFlag | (ulong)CoopDlcMask.Normalize(_sessionMask) : 0UL;

    public static void OnConnected(NetRole role)
    {
        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive)
            return;

        net.SendDlcOwned(EnsureRealMask());
        if (role == NetRole.Host)
            RecomputeHost("peer connected", rebroadcast: true);
    }

    public static void OnHostingStarted()
    {
        RecomputeHost("hosting started", rebroadcast: false);
    }

    public static void OnPreferenceChanged()
    {
        RecomputeHost("host preference changed", rebroadcast: true);
    }

    public static void OnRemoteOwnedMask(int remoteId, int owned)
    {
        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive)
            return;

        if (net.IsHost)
        {
            LobbySession.Log?.Information("[CoopDlc] Peer {Id} owns: {Dlc}", remoteId, CoopDlcMask.Describe(owned));
            RecomputeHost("peer ownership", rebroadcast: true);
        }
        else
        {
            _hostOwnedMask = owned;
            LobbySession.RequestLobbyMenuRefresh();
        }
    }

    public static void OnHostSessionMask(int mask, int revision)
    {
        var real = EnsureRealMask();
        var missing = CoopDlcMask.Normalize(mask) & ~real;
        if (missing != 0)
        {
            LobbySession.Log?.Warning(
                "[CoopDlc] Host session uses DLC this PC does not own: {Missing}. Generation may desync.",
                CoopDlcMask.Describe(missing));
        }

        _sessionMask = CoopDlcMask.Normalize(mask);
        _sessionRevision = revision;
        ApplyMask(_sessionMask, "host DLCSET r" + revision);
        LobbySession.RequestLobbyMenuRefresh();
    }

    public static void EnsureHostAppliedBeforeLaunch()
    {
        RecomputeHost("launch", rebroadcast: true);
    }

    public static void EnsureAppliedForLaunch(ulong descriptorFlags)
    {
        if ((descriptorFlags & DescriptorPresentFlag) == 0)
            return;

        var mask = CoopDlcMask.Normalize((int)(descriptorFlags & CoopDlcMask.All));
        var expected = EnsureRealMask() & mask;
        if (_sessionMask == mask && _appliedMask == expected)
            return;

        LobbySession.Log?.Warning(
            "[CoopDlc] Launch descriptor DLC {Launch} differs from applied {Applied}; re-applying",
            CoopDlcMask.Describe(mask),
            _appliedMask >= 0 ? CoopDlcMask.Describe(_appliedMask) : "none");
        _sessionMask = mask;
        ApplyMask(mask, "launch descriptor");
    }

    public static void Restore(string reason)
    {
        var applied = _appliedMask;
        _appliedMask = -1;
        _sessionMask = -1;
        _hostOwnedMask = -1;
        if (applied < 0 || _realMask < 0)
            return;

        try
        {
            var dlc = dc.tool.DLC.Class;
            dlc.installMask = _realMask;
            dlc.installMaskCacheDirty = false;
            LobbySession.Log?.Information("[CoopDlc] Restored own DLC set ({Reason}): {Dlc}", reason, CoopDlcMask.Describe(_realMask));
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[CoopDlc] Restore failed: {Message}", ex.Message);
        }
    }

    public static IReadOnlyList<(CoopDlcInfo Dlc, bool Enabled, bool Available, string MissingFor)> DescribeHostChoices()
    {
        var net = LobbySession.NetRef;
        var real = EnsureRealMask();
        var preference = MultiplayerSettingsStorage.CoopDlcPreferenceMask;
        var peers = net?.GetRemoteDlcOwnership() ?? new List<NetNode.RemoteDlcOwnership>();
        var result = new List<(CoopDlcInfo, bool, bool, string)>(CoopDlcMask.Known.Length);
        foreach (var dlc in CoopDlcMask.Known)
        {
            var missing = new List<string>();
            if ((real & dlc.Bit) == 0)
                missing.Add(LobbySession.Localize("you"));
            foreach (var peer in peers)
            {
                if (peer.OwnedMask < 0)
                    missing.Add((peer.Username ?? "?") + "?");
                else if ((peer.OwnedMask & dlc.Bit) == 0)
                    missing.Add(peer.Username ?? "?");
            }

            var available = missing.Count == 0;
            result.Add((dlc, available && (preference & dlc.Bit) != 0, available, string.Join(", ", missing)));
        }

        return result;
    }

    public static void ToggleHostPreference(int bit)
    {
        var preference = MultiplayerSettingsStorage.CoopDlcPreferenceMask;
        MultiplayerSettingsStorage.CoopDlcPreferenceMask = (preference & bit) != 0 ? preference & ~bit : preference | bit;
        OnPreferenceChanged();
    }

    static void RecomputeHost(string reason, bool rebroadcast)
    {
        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive || !net.IsHost)
            return;

        var peers = new List<int>();
        foreach (var peer in net.GetRemoteDlcOwnership())
        {
            if (peer.OwnedMask >= 0)
                peers.Add(peer.OwnedMask);
        }

        var mask = CoopDlcMask.Resolve(MultiplayerSettingsStorage.CoopDlcPreferenceMask, EnsureRealMask(), peers);
        var changed = mask != _sessionMask || _appliedMask != mask;
        if (changed)
        {
            if (LobbySession._inActualRun && _sessionMask >= 0)
            {
                LobbySession.Log?.Information(
                    "[CoopDlc] Keeping {Current} until the run ends (wanted {Wanted}, {Reason})",
                    CoopDlcMask.Describe(_sessionMask),
                    CoopDlcMask.Describe(mask),
                    reason);
            }
            else
            {
                _sessionMask = mask;
                _sessionRevision++;
                ApplyMask(mask, reason);
            }
        }

        if ((changed || rebroadcast) && _sessionMask >= 0)
            net.SendDlcSession(_sessionMask, _sessionRevision);
        if (changed)
            LobbySession.RequestLobbyMenuRefresh();
    }

    static void ApplyMask(int sessionMask, string reason)
    {
        var effective = EnsureRealMask() & CoopDlcMask.Normalize(sessionMask);
        try
        {
            var dlc = dc.tool.DLC.Class;
            dlc.installMask = effective;
            dlc.installMaskCacheDirty = false;
            _appliedMask = effective;
            LobbySession.Log?.Information("[CoopDlc] Session DLC ({Reason}): {Dlc}", reason, CoopDlcMask.Describe(effective));
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[CoopDlc] Apply failed: {Message}", ex.Message);
        }
    }

    static int EnsureRealMask()
    {
        if (_realMask >= 0)
            return _realMask;

        var mask = -1;
        try
        {
            var dlc = dc.tool.DLC.Class;
            if (_appliedMask < 0 && !dlc.installMaskCacheDirty)
                mask = dlc.installMask & CoopDlcMask.All;
        }
        catch
        {
        }

        if (mask < 0)
            mask = ReadSteamOwnedMask();
        if (mask < 0)
        {
            LobbySession.Log?.Warning("[CoopDlc] Could not read DLC ownership; treating as none");
            return 0;
        }

        _realMask = mask;
        LobbySession.Log?.Information("[CoopDlc] Own DLC: {Dlc}", CoopDlcMask.Describe(mask));
        return mask;
    }

    static int ReadSteamOwnedMask()
    {
        try
        {
            var mask = 0;
            foreach (var dlc in CoopDlcMask.Known)
            {
                var id = new AppId_t(dlc.SteamAppId);
                if (ModEntry.RunSteamNetworkingSerialized(() => SteamApps.BIsDlcInstalled(id)))
                    mask |= dlc.Bit;
            }
            return mask;
        }
        catch
        {
            return -1;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP DLC SESSION — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Игра один раз считает dc.tool.DLC.installMask из Steam (флаг installMaskCacheDirty), дальше все
 *  проверки DLC (генерация, пулы предметов/мобов, карта) читают только эту маску.
 *  Своя маска: из installMask до первого вмешательства, иначе SteamApps.BIsDlcInstalled.
 *  Хост: набор = предпочтение ∧ своё ∧ всё у клиентов → пишет installMask, dirty=false, шлёт DLCSET.
 *  Во время забега набор не меняется — новые DLCOWN только переотправляют текущий DLCSET.
 *  Клиент: DLCSET → installMask = своё ∧ набор. Недостающие DLC — предупреждение в лог.
 *  Старт забега: хост дописывает набор в RunLaunchDescriptor.DlcFlags (0x100 | mask), клиент сверяет.
 *  Конец сессии: Restore возвращает собственную маску.
 *  Включить DLC, которого у игрока нет, нельзя: эффективная маска всегда ∧ своё.
 * ═══════════════════════════════════════
*/
