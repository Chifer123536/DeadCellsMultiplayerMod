namespace DeadCellsMultiplayerMod
{
    internal static partial class LobbySession
    {
        // Crash-durable multiplayer save protection. Dead Cells parses the active slot's
        // user_N.dat (meta + current run snapshot) at every run launch; a file corrupted by an
        // earlier crash then kills the game with serializer-garbage casts
        // ("Can't cast tool._Cooldown.CdInst to level.SpotFlags") the moment a new session
        // starts — an unrecoverable crash loop for that slot. The guard:
        //   1. Rotates a two-generation backup of the MSave slot file right before every
        //      networked launch parses it (LoadingLevel phase).
        //   2. Arms a sentinel for the parse window, cleared once the hero initializes
        //      (Playing) or the session returns to Lobby/Faulted without launching.
        //   3. On the next startup after a crash inside that window, restores the newest
        //      backup automatically and quarantines the corrupt file for inspection.

        internal const string MultiplayerSaveGuardSentinelName = "launch_guard.txt";

        // Backup rotation copies the whole slot file twice. It runs on the game main thread from
        // the session state machine at every LoadingLevel transition, i.e. once per biome, so
        // repeating it when the save has not changed since the last rotation is pure stall for no
        // added protection. Identity is (length, last write time) of the live file.
        internal static long _lastRotatedSaveLength = -1;
        internal static long _lastRotatedSaveWriteTicks = -1;
        internal static int _lastRotatedSaveSlot = -1;

        internal static string GetMultiplayerSaveGuardSentinelPath()
            => GetAbsoluteSavePath(MultiplayerSaveFolderName + "/" + MultiplayerSaveGuardSentinelName);

        internal static string GetMultiplayerSaveLivePath(int slot)
            => GetAbsoluteSavePath(GetMultiplayerSaveRelativeFilePath(slot));

        internal static void NotifyRunLaunchPhaseForSaveGuard(string phase)
        {
            try
            {
                switch (phase)
                {
                    case "LoadingLevel":
                        ArmMultiplayerSaveGuard();
                        break;
                    case "Playing":
                    case "Lobby":
                    case "Faulted":
                    case "Disconnected":
                        DisarmMultiplayerSaveGuard();
                        break;
                }
            }
            catch
            {
            }
        }

        internal static void ArmMultiplayerSaveGuard()
        {
            try
            {
                EnsureMultiplayerSaveFolderExists();
                var slot = ResolveSaveSlotNumber(null);
                var live = GetMultiplayerSaveLivePath(slot);

                if (System.IO.File.Exists(live))
                {
                    var bak = live + ".bak";
                    var bak2 = live + ".bak2";
                    try
                    {
                        var info = new System.IO.FileInfo(live);
                        var length = info.Length;
                        var writeTicks = info.LastWriteTimeUtc.Ticks;
                        var unchanged = slot == _lastRotatedSaveSlot &&
                                        length == _lastRotatedSaveLength &&
                                        writeTicks == _lastRotatedSaveWriteTicks &&
                                        System.IO.File.Exists(bak);

                        if (!unchanged)
                        {
                            // Two generations: if the live file is ALREADY corrupt when we rotate,
                            // the previous good copy survives in .bak2.
                            if (System.IO.File.Exists(bak))
                                System.IO.File.Copy(bak, bak2, overwrite: true);
                            System.IO.File.Copy(live, bak, overwrite: true);

                            _lastRotatedSaveSlot = slot;
                            _lastRotatedSaveLength = length;
                            _lastRotatedSaveWriteTicks = writeTicks;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        _log?.Debug("[NetMod][SaveGuard] backup rotation failed: {Message}", ex.Message);
                    }
                }

                System.IO.File.WriteAllText(
                    GetMultiplayerSaveGuardSentinelPath(),
                    slot + "|" + System.DateTime.UtcNow.ToString("o"));
                _log?.Debug("[NetMod][SaveGuard] armed slot={Slot}", slot);
            }
            catch (System.Exception ex)
            {
                _log?.Debug("[NetMod][SaveGuard] arm failed: {Message}", ex.Message);
            }
        }

        internal static void DisarmMultiplayerSaveGuard()
        {
            try
            {
                var sentinel = GetMultiplayerSaveGuardSentinelPath();
                if (System.IO.File.Exists(sentinel))
                    System.IO.File.Delete(sentinel);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Startup self-heal for the JSON sidecar files of the multiplayer save store.
        ///
        /// ModCore's SaveManager parses <c>MSave/user_N.modded.json</c> via JObject.Parse WITHOUT a
        /// try/catch inside OnAfterLoadingSave. The multiplayer save redirect (Hook__Save_fileName)
        /// points that path into MSave while a session is active, so a sidecar corrupted by an
        /// interrupted write — the process being killed mid-shutdown while the world syncer was
        /// tearing down produces exactly the observed NUL-byte content — turns every subsequent
        /// launch into an unrecoverable Fatal Error crash loop ("Error reading JObject from
        /// JsonReader. Path '', line 1, position 2").
        ///
        /// Repair policy: a .modded.json that fails to parse is replaced with "{}" (the same
        /// content SaveManager writes for a fresh slot); a corrupt .coop.json is deleted (MUser
        /// treats a missing file as "no coop id", which is the safe state).
        /// </summary>
        internal static void SanitizeMultiplayerJsonSaves(Serilog.ILogger? logger = null)
        {
            var log = logger ?? _log;
            try
            {
                var folder = GetAbsoluteSavePath(MultiplayerSaveFolderName);
                if (!System.IO.Directory.Exists(folder))
                    return;

                foreach (var path in System.IO.Directory.GetFiles(folder, "*.json"))
                {
                    try
                    {
                        if (IsValidJsonObjectFile(path))
                            continue;

                        var name = System.IO.Path.GetFileName(path);
                        if (name.EndsWith(".modded.json", System.StringComparison.Ordinal))
                        {
                            // Keep the slot alive: SaveManager only needs a parseable object here.
                            System.IO.File.Copy(path, path + ".corrupt", overwrite: true);
                            System.IO.File.WriteAllText(path, "{}");
                            log?.Warning("[NetMod][SaveGuard] Repaired corrupt modded save sidecar: {Path}", name);
                        }
                        else if (name.EndsWith(".coop.json", System.StringComparison.Ordinal))
                        {
                            // Coop metadata is regenerable; a missing file is handled by MUser.
                            System.IO.File.Move(path, path + ".corrupt", overwrite: true);
                            log?.Warning("[NetMod][SaveGuard] Quarantined corrupt coop metadata: {Path}", name);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        log?.Debug("[NetMod][SaveGuard] sidecar sanitize failed for {Path}: {Message}", path, ex.Message);
                    }
                }
            }
            catch (System.Exception ex)
            {
                log?.Debug("[NetMod][SaveGuard] sanitize sweep failed: {Message}", ex.Message);
            }
        }

        private static bool IsValidJsonObjectFile(string path)
        {
            try
            {
                var text = System.IO.File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                // Fast rejection of NUL-padded files without a parse attempt.
                for (var i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\0')
                        return false;
                }

                _ = Newtonsoft.Json.Linq.JObject.Parse(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void RunMultiplayerSaveStartupRecovery(Serilog.ILogger? logger = null)
        {
            var log = logger ?? _log;

            try
            {
                var sentinel = GetMultiplayerSaveGuardSentinelPath();
                if (!System.IO.File.Exists(sentinel))
                    return;

                var slot = ResolveSaveSlotNumber(null);
                try
                {
                    var payload = System.IO.File.ReadAllText(sentinel);
                    var sep = payload.IndexOf('|');
                    if (sep > 0 && int.TryParse(payload.AsSpan(0, sep), out var parsedSlot) && parsedSlot >= 0)
                        slot = parsedSlot;
                }
                catch
                {
                }

                var live = GetMultiplayerSaveLivePath(slot);
                var bak = live + ".bak";
                var bak2 = live + ".bak2";
                var source = System.IO.File.Exists(bak) ? bak : (System.IO.File.Exists(bak2) ? bak2 : null);

                if (source == null)
                {
                    log?.Warning(
                        "[NetMod][SaveGuard] Previous multiplayer launch crashed while reading slot {Slot}, but no backup exists yet. Save left untouched.",
                        slot);
                }
                else
                {
                    try
                    {
                        if (System.IO.File.Exists(live))
                        {
                            var quarantine = live + ".corrupt-" + System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                            System.IO.File.Move(live, quarantine);
                            log?.Warning("[NetMod][SaveGuard] Quarantined suspect save: {Path}", quarantine);
                        }

                        System.IO.File.Copy(source, live, overwrite: true);
                        log?.Warning(
                            "[NetMod][SaveGuard] Previous multiplayer launch crashed while reading slot {Slot}; restored the save from backup.",
                            slot);
                        try
                        {
                            MultiplayerModUI.lifeUI.MultiplayerUI.PushSystemMessage(
                                "Multiplayer save was restored from backup after a crash.");
                        }
                        catch
                        {
                        }
                    }
                    catch (System.Exception ex)
                    {
                        log?.Warning("[NetMod][SaveGuard] restore failed: {Message}", ex.Message);
                    }
                }

                try { System.IO.File.Delete(sentinel); } catch { }
            }
            catch (System.Exception ex)
            {
                log?.Debug("[NetMod][SaveGuard] startup recovery failed: {Message}", ex.Message);
            }
        }
    }
}
