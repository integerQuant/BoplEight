using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BoplEight.Lobby;
using BoplEight.Match;
using BoplEight.Protocol;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine.SceneManagement;
using SteamLobby = Steamworks.Data.Lobby;

namespace BoplEight.Runtime
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("BoplBattle.exe")]
    public sealed class BoplEightPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "io.opencode.bopleight";
        public const string PluginName = "BoplEight";
        public const string PluginVersion = "1.0.1";

        // Patches are only applied to the game assembly inspected for this release.
        private const string SupportedGameAssemblySha256 = "06A154AF64AD962E534587058219FB94216C5CE53605BB9AF5F77CB433A4AE07";
        private static readonly string[] MetadataKeys =
        {
            LobbyMetadata.PluginIdKey,
            LobbyMetadata.PluginVersionKey,
            LobbyMetadata.ProtocolVersionKey,
            LobbyMetadata.GameAssemblyHashKey,
            LobbyMetadata.MinimumPlayersKey,
            LobbyMetadata.MaximumPlayersKey
        };
        private static readonly FieldInfo GameSessionSelfField = AccessTools.Field(typeof(GameSessionHandler), "selfRef");
        private static readonly FieldInfo PendingSceneLoadField = AccessTools.Field(typeof(GameSessionHandler), "SceneLoad");
        private static readonly FieldInfo GameLobbyJoinInProgressField = AccessTools.Field(typeof(SteamManager), "isJoiningAlobbyAtm");
        private static readonly FieldInfo GameMatchmakingInProgressField = AccessTools.Field(typeof(SteamManager), "currentlyInMatchmaking");
        private static readonly MethodInfo CreateLobbyMethod = AccessTools.Method(typeof(SteamManager), "createLobby");
        private static readonly MethodInfo FindAndJoinLobbyMethod = AccessTools.Method(typeof(SteamManager), "FindAndJoinLobby");
        private static readonly MethodInfo FindAndMergeLobbyMethod = AccessTools.Method(typeof(SteamManager), "FindAndMergeToYourLobby");
        private static readonly MethodInfo JoinLobbyMethod = AccessTools.Method(typeof(SteamManager), "JoinLobby");
        private static readonly LobbyJoinGate MatchmakingSearches = new LobbyJoinGate();
        private static readonly LobbyJoinGate SteamLobbyJoins = new LobbyJoinGate();
        private static readonly PendingLobbyJoinState PendingLobbyJoin = new PendingLobbyJoinState();
        private const float LobbyMetadataTimeoutSeconds = 5f;
        private static bool lobbyReplacementPending;
        private bool lobbyDataChangedSubscribed;

        internal static ManualLogSource Log;
        internal static ModIdentity Identity;

        internal static void MarkLobbyReplacementPending()
        {
            lobbyReplacementPending = SteamClient.IsValid;
        }

        internal static void MarkLobbyReplacementCompleted()
        {
            lobbyReplacementPending = false;
        }

        private void Awake()
        {
            Log = Logger;

            string gameAssemblyHash;
            try
            {
                gameAssemblyHash = ComputeGameAssemblyHash();
            }
            catch (Exception exception)
            {
                Logger.LogError("BoplEight could not verify Assembly-CSharp.dll and will stay disabled: " + exception.Message);
                enabled = false;
                return;
            }

            if (!string.Equals(gameAssemblyHash, SupportedGameAssemblySha256, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogError("BoplEight supports game assembly " + SupportedGameAssemblySha256 + ", but found " + gameAssemblyHash + ". No patches were applied.");
                enabled = false;
                return;
            }

            Identity = new ModIdentity(PluginGuid, PluginVersion, ProtocolConstants.Version, gameAssemblyHash);
            Constants.MAX_PLAYERS = ProtocolConstants.MaximumPlayers;

            var harmony = new Harmony(PluginGuid);
            try
            {
                harmony.PatchAll(typeof(BoplEightPlugin).Assembly);
                VerifyCriticalPatches();
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                Constants.MAX_PLAYERS = 4;
                Logger.LogError("BoplEight could not install its complete runtime patch set and was disabled: " + exception);
                enabled = false;
                return;
            }

            SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChanged;
            lobbyDataChangedSubscribed = true;
            Logger.LogInfo("BoplEight 2-8 player runtime enabled. Modded replay recording is disabled during BoplEight matches.");
        }

        private void LateUpdate()
        {
            CheckPendingLobbyJoinTimeout();

            string reason;
            if (!BoplEightSession.TryTakeMatchTermination(out reason))
            {
                return;
            }

            Logger.LogError(reason);
            if (SteamManager.instance == null)
            {
                if (!ForceLocalTeardown())
                {
                    BoplEightSession.RequestMatchTermination(reason);
                }
                return;
            }

            try
            {
                GameSessionHandler.LeaveGame(true);
            }
            catch (Exception exception)
            {
                Logger.LogError("BoplEight failed during deferred match teardown: " + exception);
                if (!ForceLocalTeardown())
                {
                    BoplEightSession.RequestMatchTermination(reason);
                }
            }
        }

        private void OnDestroy()
        {
            if (lobbyDataChangedSubscribed)
            {
                SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChanged;
                lobbyDataChangedSubscribed = false;
            }

            CancelPendingLobbyJoin();
        }

        private static void OnLobbyDataChanged(SteamLobby lobby)
        {
            if (!PendingLobbyJoin.TryComplete((ulong)lobby.Id))
            {
                return;
            }
            SetGameLobbyJoinInProgress(false);

            string reason;
            LobbyJoinDecision decision = EvaluateModdedLobby(lobby, true, out reason);
            if (decision != LobbyJoinDecision.Accept)
            {
                RejectLobbyJoin(reason, "Rejected lobby after refreshing Steam metadata: ");
                return;
            }

            if (SteamManager.instance == null || JoinLobbyMethod == null)
            {
                RejectLobbyJoin("The Steam lobby is unavailable.", "Could not resume lobby join: ");
                return;
            }

            Log.LogInfo("Verified refreshed BoplEight metadata for lobby " + lobby.Id + ".");
            JoinLobbyMethod.Invoke(SteamManager.instance, new object[] { lobby });
        }

        private static void CheckPendingLobbyJoinTimeout()
        {
            if (!PendingLobbyJoin.TryTakeTimeout(UnityEngine.Time.realtimeSinceStartup))
            {
                return;
            }
            SetGameLobbyJoinInProgress(false);

            RejectLobbyJoin(
                "Steam did not return BoplEight lobby compatibility data in time.",
                "Timed out before joining lobby: ");
        }

        private bool ForceLocalTeardown()
        {
            bool completed = true;
            try
            {
                object sessionHandler = GameSessionSelfField == null ? null : GameSessionSelfField.GetValue(null);
                UnityEngine.AsyncOperation pendingSceneLoad = sessionHandler == null || PendingSceneLoadField == null
                    ? null
                    : PendingSceneLoadField.GetValue(sessionHandler) as UnityEngine.AsyncOperation;
                if (pendingSceneLoad != null)
                {
                    pendingSceneLoad.allowSceneActivation = false;
                }
            }
            catch (Exception exception)
            {
                completed = false;
                Logger.LogError("BoplEight could not suspend an in-flight scene load during fallback teardown: " + exception);
            }

            SteamManager manager = SteamManager.instance;
            if (manager != null)
            {
                var connections = new List<SteamConnection>(manager.connectedPlayers);
                for (var index = 0; index < connections.Count; index++)
                {
                    try
                    {
                        if (connections[index].Connected
                            && !connections[index].Connection.Close(false, 0, "BoplEight fallback teardown"))
                        {
                            completed = false;
                        }
                    }
                    catch (Exception exception)
                    {
                        completed = false;
                        Logger.LogError("BoplEight could not close a Steam peer during fallback teardown: " + exception);
                    }
                }

                try
                {
                    if (!lobbyReplacementPending)
                    {
                        manager.LeaveLobby();
                    }

                    manager.connectedPlayers.Clear();
                }
                catch (Exception exception)
                {
                    completed = false;
                    Logger.LogError("BoplEight could not leave the Steam lobby during fallback teardown: " + exception);
                    try
                    {
                        manager.currentLobby.Leave();
                        if (SteamClient.IsValid && CreateLobbyMethod != null)
                        {
                            CreateLobbyMethod.Invoke(manager, null);
                            lobbyReplacementPending = true;
                        }
                    }
                    catch (Exception recoveryException)
                    {
                        Logger.LogError("BoplEight could not recreate the Steam lobby during fallback teardown: " + recoveryException);
                    }
                }
            }

            try
            {
                if (SteamManager.networkClientHandle != null)
                {
                    SteamManager.networkClientHandle.ClearClients();
                    SteamManager.networkClientHandle.DeInit();
                }
            }
            catch (Exception exception)
            {
                completed = false;
                Logger.LogError("BoplEight could not deinitialize the network host during fallback teardown: " + exception);
            }

            try
            {
                if (GameLobby.isOnlineGame && PlayerHandler.Get() != null)
                {
                    PlayerHandler.Get().PlayerList().Clear();
                }
            }
            catch (Exception exception)
            {
                completed = false;
                Logger.LogError("BoplEight could not clear players during fallback teardown: " + exception);
            }

            BoplEightSession.Clear();
            try
            {
                Updater.PreLevelLoad();
                SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
                Updater.PostLevelLoad();
            }
            catch (Exception exception)
            {
                completed = false;
                Logger.LogError("BoplEight could not load the main menu during fallback teardown: " + exception);
            }

            return completed;
        }

        internal static void ConfigureModdedLobby(SteamLobby lobby)
        {
            if (Identity == null || !SteamClient.IsValid || (ulong)lobby.Id == 0)
            {
                Log.LogWarning("Skipped BoplEight lobby configuration before Steam and the plugin identity were initialized.");
                return;
            }

            lobby.MaxMembers = ProtocolConstants.MaximumPlayers;
            IDictionary<string, string> metadata = LobbyMetadata.Create(Identity);
            foreach (KeyValuePair<string, string> entry in metadata)
            {
                if (!lobby.SetData(entry.Key, entry.Value))
                {
                    Log.LogWarning("Steam rejected BoplEight lobby metadata key " + entry.Key + ".");
                }
            }

            Log.LogInfo("Configured BoplEight lobby " + lobby.Id + " for 2-" + lobby.MaxMembers + " modded players.");
        }

        internal static bool IsBoplEightLobby(SteamLobby lobby)
        {
            if (!SteamClient.IsValid || (ulong)lobby.Id == 0)
            {
                return false;
            }

            try
            {
                return !string.IsNullOrEmpty(lobby.GetData(LobbyMetadata.PluginIdKey));
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryValidateModdedLobby(SteamLobby lobby, out string reason)
        {
            return EvaluateModdedLobby(lobby, true, out reason) == LobbyJoinDecision.Accept;
        }

        internal static LobbyJoinDecision EvaluateModdedLobby(SteamLobby lobby, bool metadataRefreshCompleted, out string reason)
        {
            if (Identity == null || !SteamClient.IsValid || (ulong)lobby.Id == 0)
            {
                reason = "BoplEight has not initialized its compatibility identity.";
                return LobbyJoinDecision.Reject;
            }

            try
            {
                var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < MetadataKeys.Length; index++)
                {
                    string key = MetadataKeys[index];
                    metadata[key] = lobby.GetData(key);
                }

                return LobbyMetadata.EvaluateForJoin(metadata, Identity, metadataRefreshCompleted, out reason);
            }
            catch (Exception exception)
            {
                reason = "Steam could not read BoplEight lobby compatibility data.";
                Log.LogWarning(reason + " " + exception.Message);
                return LobbyJoinDecision.Reject;
            }
        }

        internal static void DeferLobbyJoinUntilMetadataArrives(SteamLobby lobby)
        {
            ulong lobbyId = (ulong)lobby.Id;
            PendingLobbyJoin.Begin(lobbyId, UnityEngine.Time.realtimeSinceStartup, LobbyMetadataTimeoutSeconds);
            SetGameLobbyJoinInProgress(true);

            bool requested;
            try
            {
                requested = lobby.Refresh();
            }
            catch (Exception exception)
            {
                Log.LogWarning("Steam threw while requesting BoplEight lobby metadata: " + exception.Message);
                requested = false;
            }

            if (requested)
            {
                Log.LogInfo("Waiting for Steam compatibility metadata for lobby " + lobby.Id + ".");
                return;
            }

            if (PendingLobbyJoin.TryComplete(lobbyId))
            {
                SetGameLobbyJoinInProgress(false);
                RejectLobbyJoin(
                    "Steam could not request BoplEight lobby compatibility data.",
                    "Could not defer lobby join: ");
            }
        }

        internal static void CancelPendingLobbyJoin()
        {
            if (PendingLobbyJoin.TryCancel())
            {
                SetGameLobbyJoinInProgress(false);
            }
        }

        internal static bool IsGameLobbyJoinInProgress()
        {
            return (GameLobbyJoinInProgressField != null
                    && (bool)GameLobbyJoinInProgressField.GetValue(null))
                || SteamLobbyJoins.IsActive;
        }

        internal static bool HasPendingLobbyJoin()
        {
            return PendingLobbyJoin.IsActive;
        }

        internal static bool IsGameMatchmakingInProgress()
        {
            return (GameMatchmakingInProgressField != null
                    && (bool)GameMatchmakingInProgressField.GetValue(null))
                || MatchmakingSearches.IsActive;
        }

        internal static bool TryMarkMatchmakingSearchStarted()
        {
            return MatchmakingSearches.TryEnter();
        }

        internal static void MarkMatchmakingSearchCompleted()
        {
            MatchmakingSearches.Exit();
        }

        internal static bool TryMarkSteamLobbyJoinStarted()
        {
            return SteamLobbyJoins.TryEnter();
        }

        internal static void MarkSteamLobbyJoinCompleted()
        {
            SteamLobbyJoins.Exit();
        }

        internal static void RejectLobbyJoin(string reason, string logPrefix)
        {
            if (string.IsNullOrEmpty(reason))
            {
                reason = "This lobby does not have BoplEight installed.";
            }

            Log.LogWarning(logPrefix + reason);
            ErrorHandlingTextDisconnect.SetMainMenuLogMessage(reason);
        }

        internal static bool IsCurrentLobbyOwner(NetIdentity sender)
        {
            if (!sender.IsSteamId)
            {
                return false;
            }

            if (!SteamClient.IsValid)
            {
                return false;
            }

            var managerField = AccessTools.Field(typeof(SteamManager), "instance");
            var lobbyField = AccessTools.Field(typeof(SteamManager), "currentLobby");
            if (managerField == null || lobbyField == null)
            {
                return false;
            }

            object manager = managerField.GetValue(null);
            if (manager == null)
            {
                return false;
            }

            var lobby = (SteamLobby)lobbyField.GetValue(manager);
            if ((ulong)lobby.Id == 0)
            {
                return false;
            }

            return (ulong)lobby.Owner.Id == (ulong)sender.SteamId;
        }

        internal static bool IsCurrentLobbyMember(ulong steamId)
        {
            if (steamId == 0
                || !SteamClient.IsValid
                || SteamManager.instance == null
                || (ulong)SteamManager.instance.currentLobby.Id == 0)
            {
                return false;
            }

            foreach (Friend member in SteamManager.instance.currentLobby.Members)
            {
                if ((ulong)member.Id == steamId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeGameAssemblyHash()
        {
            string assemblyPath = Path.Combine(Paths.GameRootPath, "BoplBattle_Data", "Managed", "Assembly-CSharp.dll");
            using (var stream = File.OpenRead(assemblyPath))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static void EnableGameModCompatibilityFlag()
        {
            const string compatibilityFieldName = "clientSideMods_you_can_increment_this_to_enable_matchmaking_for_your_mods__please_dont_use_it_to_cheat_thats_really_cringe_especially_if_its_desyncing_others___you_didnt_even_win_on_your_opponents_screen___I_cannot_imagine_a_sadder_existence";
            var field = AccessTools.Field(typeof(CharacterSelectHandler_online), compatibilityFieldName);
            if (field == null)
            {
                LoggerFallback("The game mod-compatibility field was not found. Matchmaking may reject BoplEight lobbies.");
                return;
            }

            if (!field.IsStatic || field.FieldType != typeof(int))
            {
                LoggerFallback("The game mod-compatibility field has an unexpected shape. Matchmaking was left unchanged.");
                return;
            }

            field.SetValue(null, (int)field.GetValue(null) + 1);
        }

        private static void VerifyCriticalPatches()
        {
            if (GameLobbyJoinInProgressField == null
                || !GameLobbyJoinInProgressField.IsStatic
                || GameLobbyJoinInProgressField.FieldType != typeof(bool))
            {
                throw new MissingFieldException("The game's lobby join guard was not found.");
            }

            if (GameMatchmakingInProgressField == null
                || !GameMatchmakingInProgressField.IsStatic
                || GameMatchmakingInProgressField.FieldType != typeof(bool))
            {
                throw new MissingFieldException("The game's matchmaking guard was not found.");
            }

            MethodBase[] criticalMethods =
            {
                FindAndJoinLobbyMethod,
                FindAndMergeLobbyMethod,
                JoinLobbyMethod,
                AccessTools.Method(typeof(SteamLobby), "Join"),
                AccessTools.Method(typeof(SteamManager), "HostGame"),
                AccessTools.Method(typeof(CharacterSelectHandler_online), "ForceStartGame"),
                AccessTools.Method(typeof(SteamManager), "InitNetworkClient"),
                AccessTools.Method(typeof(SteamManager), "HostNextLevel"),
                AccessTools.Method(typeof(SteamManager), "ForceLoadNextLevel"),
                AccessTools.Method(typeof(CharacterStatsList), "OnNextLevelWasSuggested"),
                AccessTools.Method(typeof(CharacterStatsList), "ForceNextLevelImmediately"),
                AccessTools.Method(typeof(Host), "ProcessNetworkPackets"),
                AccessTools.Method(typeof(Host), "Update"),
                AccessTools.Method(typeof(Updater), "TickSimulation"),
                AccessTools.Method(typeof(GameSessionHandler), "SpawnPlayers"),
                AccessTools.Method(typeof(SteamSocket), "OnMessage")
            };

            for (var index = 0; index < criticalMethods.Length; index++)
            {
                MethodBase method = criticalMethods[index];
                if (method == null)
                {
                    throw new MissingMethodException("A required BoplEight game method was not found.");
                }

                Patches patches = Harmony.GetPatchInfo(method);
                bool foundOwner = false;
                if (patches != null)
                {
                    foreach (string owner in patches.Owners)
                    {
                        if (string.Equals(owner, PluginGuid, StringComparison.Ordinal))
                        {
                            foundOwner = true;
                            break;
                        }
                    }
                }

                if (!foundOwner)
                {
                    throw new InvalidOperationException("A required BoplEight patch was not installed on " + method.DeclaringType.FullName + "." + method.Name + ".");
                }
            }

            Log.LogInfo("Verified all critical roster, frame, simulation, spawn, and packet patches.");
        }

        private static void SetGameLobbyJoinInProgress(bool value)
        {
            GameLobbyJoinInProgressField.SetValue(null, value);
        }

        private static void LoggerFallback(string message)
        {
            if (Log != null)
            {
                Log.LogWarning(message);
            }
        }
    }

    internal static class BoplEightSession
    {
        private static readonly object Sync = new object();
        private static readonly RosterState State = new RosterState();
        private static bool restoreReplayRecording;
        private static string pendingTerminationReason;

        internal static StartRoster ActiveRoster
        {
            get
            {
                lock (Sync)
                {
                    return State.ActiveRoster;
                }
            }
        }

        internal static bool TryActivateStartRoster(StartRoster roster, ulong senderSteamId, out string reason)
        {
            lock (Sync)
            {
                bool firstRoster = State.ActiveRoster == null;
                if (!State.TryActivate(roster, senderSteamId, out reason))
                {
                    return false;
                }

                if (firstRoster)
                {
                    restoreReplayRecording = Host.recordReplay;
                    Host.recordReplay = false;
                }

                return true;
            }
        }

        internal static void RequestMatchTermination(string reason)
        {
            lock (Sync)
            {
                if (string.IsNullOrEmpty(pendingTerminationReason))
                {
                    pendingTerminationReason = string.IsNullOrEmpty(reason)
                        ? "BoplEight ended the match after a synchronization failure."
                        : reason;
                }
            }
        }

        internal static bool TryTakeMatchTermination(out string reason)
        {
            lock (Sync)
            {
                reason = pendingTerminationReason;
                pendingTerminationReason = null;
                return !string.IsNullOrEmpty(reason);
            }
        }

        internal static void Clear()
        {
            lock (Sync)
            {
                State.Clear();
                if (restoreReplayRecording)
                {
                    Host.recordReplay = true;
                }

                restoreReplayRecording = false;
                pendingTerminationReason = null;
            }

            FrameRuntime.Reset();
            PeerCompatibility.Reset();
            RosterStartCoordinator.Reset();
            RosterRuntime.Reset();
        }
    }

    [HarmonyPatch(typeof(SteamManager), "OnLobbyCreatedCallback")]
    internal static class SteamManagerOnLobbyCreatedPatch
    {
        private static void Postfix(Result __0, SteamLobby __1)
        {
            BoplEightPlugin.MarkLobbyReplacementCompleted();
            if (__0 == Result.OK)
            {
                BoplEightPlugin.ConfigureModdedLobby(__1);
            }
        }
    }

    [HarmonyPatch]
    internal static class SteamManagerMatchmakingSearchPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SteamManager), "FindAndJoinLobby");
            yield return AccessTools.Method(typeof(SteamManager), "FindAndMergeToYourLobby");
        }

        private static bool Prefix(ref System.Threading.Tasks.Task<bool> __result, out bool __state)
        {
            __state = false;
            if (BoplEightPlugin.HasPendingLobbyJoin()
                || BoplEightPlugin.IsGameLobbyJoinInProgress()
                || !BoplEightPlugin.TryMarkMatchmakingSearchStarted())
            {
                __result = System.Threading.Tasks.Task.FromResult(false);
                return false;
            }

            __state = true;
            return true;
        }

        private static void Postfix(bool __state, ref System.Threading.Tasks.Task<bool> __result)
        {
            if (__state)
            {
                __result = TrackCompletion(__result);
            }
        }

        private static async System.Threading.Tasks.Task<bool> TrackCompletion(System.Threading.Tasks.Task<bool> search)
        {
            try
            {
                return await search;
            }
            finally
            {
                BoplEightPlugin.MarkMatchmakingSearchCompleted();
            }
        }
    }

    [HarmonyPatch(typeof(SteamLobby), "Join")]
    internal static class SteamLobbyJoinPatch
    {
        private static bool Prefix(ref System.Threading.Tasks.Task<RoomEnter> __result, out bool __state)
        {
            __state = false;
            if (BoplEightPlugin.HasPendingLobbyJoin())
            {
                __result = System.Threading.Tasks.Task.FromResult(RoomEnter.Error);
                return false;
            }

            if (!BoplEightPlugin.TryMarkSteamLobbyJoinStarted())
            {
                __result = System.Threading.Tasks.Task.FromResult(RoomEnter.Error);
                return false;
            }

            __state = true;
            return true;
        }

        private static void Postfix(bool __state, ref System.Threading.Tasks.Task<RoomEnter> __result)
        {
            if (__state)
            {
                __result = TrackCompletion(__result);
            }
        }

        private static async System.Threading.Tasks.Task<RoomEnter> TrackCompletion(System.Threading.Tasks.Task<RoomEnter> join)
        {
            try
            {
                return await join;
            }
            finally
            {
                BoplEightPlugin.MarkSteamLobbyJoinCompleted();
            }
        }
    }

    [HarmonyPatch(typeof(SteamManager), "JoinLobby")]
    internal static class SteamManagerJoinLobbyPatch
    {
        private static bool Prefix(SteamLobby lobby)
        {
            BoplEightPlugin.CancelPendingLobbyJoin();
            if (BoplEightPlugin.IsGameMatchmakingInProgress())
            {
                BoplEightPlugin.RejectLobbyJoin(
                    "Wait for matchmaking to finish stopping before joining a BoplEight lobby invite.",
                    "Rejected lobby invite during matchmaking: ");
                return false;
            }

            if (BoplEightPlugin.IsGameLobbyJoinInProgress())
            {
                BoplEightPlugin.RejectLobbyJoin(
                    "Another Steam lobby join is already in progress.",
                    "Rejected concurrent lobby join: ");
                return false;
            }

            string reason;
            LobbyJoinDecision decision = BoplEightPlugin.EvaluateModdedLobby(lobby, false, out reason);
            if (decision == LobbyJoinDecision.Accept)
            {
                return true;
            }

            if (decision == LobbyJoinDecision.AwaitMetadata)
            {
                BoplEightPlugin.DeferLobbyJoinUntilMetadataArrives(lobby);
                return false;
            }

            BoplEightPlugin.RejectLobbyJoin(reason, "Rejected lobby before joining: ");
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamManager), "OnLobbyEnteredCallback")]
    internal static class SteamManagerOnLobbyEnteredPatch
    {
        private static bool Prefix(SteamLobby __0)
        {
            BoplEightPlugin.CancelPendingLobbyJoin();
            if (!BoplEightPlugin.IsBoplEightLobby(__0))
            {
                if (__0.Owner.IsMe)
                {
                    BoplEightPlugin.ConfigureModdedLobby(__0);
                    BoplEightSession.Clear();
                    return true;
                }

                const string lobbyReason = "This lobby does not have BoplEight installed.";
                BoplEightPlugin.Log.LogWarning("Rejected incompatible lobby: " + lobbyReason);
                ErrorHandlingTextDisconnect.SetMainMenuLogMessage(lobbyReason);
                BoplEightSession.Clear();
                __0.Leave();
                return false;
            }

            string reason;
            if (BoplEightPlugin.TryValidateModdedLobby(__0, out reason))
            {
                BoplEightSession.Clear();
                return true;
            }

            BoplEightPlugin.Log.LogWarning("Rejected incompatible BoplEight lobby: " + reason);
            BoplEightSession.Clear();
            __0.Leave();
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamSocket), "OnMessage")]
    internal static class SteamSocketOnMessagePatch
    {
        private const int MaximumBoplEightPacketBytes = 256;

        private static bool Prefix(NetIdentity __1, IntPtr __2, int __3)
        {
            if (__2 == IntPtr.Zero || __3 <= 0)
            {
                BoplEightPlugin.Log.LogWarning("Rejected a network packet with an invalid pointer or size.");
                return false;
            }

            if (!ValidateVanillaLobbyPacket(__2, __3))
            {
                BoplEightPlugin.Log.LogWarning("Rejected invalid lobby selection data before vanilla dispatch.");
                return false;
            }

            if (FrameRuntime.TryDispatchGameplayPacket(__1, __2, __3))
            {
                return false;
            }

            if (__3 < 4)
            {
                if (PacketRouter.IsReservedBoplEightMessageType(Marshal.ReadByte(__2)))
                {
                    BoplEightPlugin.Log.LogWarning("Rejected a truncated BoplEight packet before vanilla dispatch.");
                    return false;
                }

                return true;
            }

            var header = new byte[4];
            Marshal.Copy(__2, header, 0, header.Length);
            if (!PacketRouter.IsReservedBoplEightMessageType(header[0])
                || header[1] != (byte)(ProtocolConstants.Magic & 0xff)
                || header[2] != (byte)(ProtocolConstants.Magic >> 8))
            {
                return true;
            }

            if (__3 > MaximumBoplEightPacketBytes)
            {
                BoplEightPlugin.Log.LogWarning("Rejected an oversized BoplEight packet before vanilla dispatch.");
                return false;
            }

            var packet = new byte[__3];
            Marshal.Copy(__2, packet, 0, packet.Length);
            PacketRoute route = PacketRouter.Route(packet);
            if (route.Kind == PacketRouteKind.RejectedBoplEightPacket)
            {
                BoplEightPlugin.Log.LogWarning("Rejected BoplEight packet: " + route.Reason);
                return false;
            }

            if (route.Kind == PacketRouteKind.NotBoplEightPacket)
            {
                BoplEightPlugin.Log.LogWarning("Rejected a claimed BoplEight packet before vanilla dispatch.");
                return false;
            }

            if (!__1.IsSteamId)
            {
                BoplEightPlugin.Log.LogWarning("Rejected a BoplEight packet without a valid Steam sender.");
                return false;
            }

            ulong senderSteamId = (ulong)__1.SteamId;
            SteamConnection senderConnection = RosterRuntime.FindConnection(senderSteamId);
            if (!BoplEightPlugin.IsCurrentLobbyMember(senderSteamId)
                || senderConnection == null
                || !senderConnection.Connected)
            {
                BoplEightPlugin.Log.LogWarning("Rejected a BoplEight packet from outside the connected lobby mesh.");
                return false;
            }

            if (route.Kind == PacketRouteKind.Hello)
            {
                PeerCompatibility.OnHello(senderSteamId, route.ManifestToken);
                return false;
            }

            if (route.Kind == PacketRouteKind.HelloAck)
            {
                PeerCompatibility.OnHelloAcknowledged(senderSteamId, route.ManifestToken);
                return false;
            }

            if (route.Kind == PacketRouteKind.RosterAck)
            {
                RosterStartCoordinator.ReceiveAcknowledgement(route.SequenceNumber, route.RosterToken, senderSteamId);
                return false;
            }

            if (route.Kind == PacketRouteKind.CommitRoster)
            {
                if (!BoplEightPlugin.IsCurrentLobbyOwner(__1))
                {
                    BoplEightPlugin.Log.LogWarning("Rejected a roster commit from a non-owner connection.");
                    return false;
                }

                RosterStartCoordinator.ReceiveCommit(route.SequenceNumber, route.RosterToken, senderSteamId);
                return false;
            }

            if (route.Kind == PacketRouteKind.CommitAck)
            {
                RosterStartCoordinator.ReceiveCommitAcknowledgement(route.SequenceNumber, route.RosterToken, senderSteamId);
                return false;
            }

            if (route.Kind == PacketRouteKind.RejectRoster)
            {
                RosterStartCoordinator.ReceiveRejection(route.SequenceNumber, route.RosterToken, senderSteamId);
                return false;
            }

            if (route.Kind != PacketRouteKind.StartRoster)
            {
                return false;
            }

            if (!BoplEightPlugin.IsCurrentLobbyOwner(__1))
            {
                BoplEightPlugin.Log.LogWarning("Rejected a BoplEight start roster from a non-owner connection.");
                return false;
            }

            string reason;
            if (!RosterStartCoordinator.PrepareRemote(route.StartRoster, senderSteamId, out reason))
            {
                BoplEightPlugin.Log.LogWarning("Rejected BoplEight start roster: " + reason);
                return false;
            }

            return false;
        }

        private static bool ValidateVanillaLobbyPacket(IntPtr data, int size)
        {
            if (size != NetworkTools.LobbyStateUpdateSize
                && size != NetworkTools.SimpleLobbyReadySize
                && size != NetworkTools.LobbyReadyPacketSize
                && size != NetworkTools.LobbyInitPacketSize)
            {
                return true;
            }

            var packet = new byte[size];
            Marshal.Copy(data, packet, 0, size);
            int abilityCount = SteamManager.instance == null
                || SteamManager.instance.abilityIcons == null
                || SteamManager.instance.abilityIcons.sprites == null
                ? 0
                : SteamManager.instance.abilityIcons.sprites.Count;
            if (size == NetworkTools.LobbyStateUpdateSize)
            {
                return packet[0] < ProtocolConstants.MaximumPlayers
                    && packet[1] < ProtocolConstants.MaximumPlayers
                    && packet[2] <= 1;
            }

            if (size == NetworkTools.SimpleLobbyReadySize)
            {
                return packet[0] < abilityCount
                    && packet[1] < abilityCount
                    && packet[2] < abilityCount;
            }

            if (size == NetworkTools.LobbyReadyPacketSize)
            {
                return packet[0] < ProtocolConstants.MaximumPlayers
                    && packet[1] < ProtocolConstants.MaximumPlayers
                    && packet[2] < abilityCount
                    && packet[3] < abilityCount
                    && packet[4] < abilityCount
                    && packet[5] <= 1
                    && packet[6] <= 1;
            }

            return packet[0] >= 1 && packet[0] <= ProtocolConstants.AbilityCount;
        }
    }

    [HarmonyPatch(typeof(SteamManager), "StartMatchmaking")]
    internal static class SteamManagerStartMatchmakingPatch
    {
        private static void Prefix()
        {
            BoplEightPlugin.CancelPendingLobbyJoin();
        }
    }

    [HarmonyPatch(typeof(SteamManager), "LeaveLobby")]
    internal static class SteamManagerLeaveLobbyPatch
    {
        private static void Prefix()
        {
            BoplEightPlugin.CancelPendingLobbyJoin();
            BoplEightSession.Clear();
        }

        private static void Postfix()
        {
            BoplEightPlugin.MarkLobbyReplacementPending();
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "CheckForMods")]
    internal static class CharacterSelectCheckForModsPatch
    {
        private static void Postfix()
        {
            CharacterSelectHandler_online.hasMods = true;
        }
    }
}
