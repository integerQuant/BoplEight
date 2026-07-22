using System;
using System.Collections.Generic;
using System.Reflection;
using BoplEight.Protocol;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace BoplEight.Runtime
{
    internal static class RosterStartCoordinator
    {
        private const float PreparationTimeoutSeconds = 10f;
        private const float CommitTimeoutSeconds = 10f;
        private const float CommitRetrySeconds = 0.5f;
        private static readonly object Sync = new object();
        private static readonly FieldInfo IsStartingGameField = AccessTools.Field(typeof(CharacterSelectHandler_online), "isStartingAGame");
        private static readonly HashSet<ulong> AcknowledgedPeers = new HashSet<ulong>();
        private static readonly HashSet<ulong> CommitAcknowledgedPeers = new HashSet<ulong>();
        private static StartRoster pendingRoster;
        private static ulong pendingRosterToken;
        private static ulong pendingOwnerSteamId;
        private static bool localPlayerIsCoordinator;
        private static bool pendingNextLevel;
        private static bool commitWasSent;
        private static float deadline;
        private static float nextCommitBroadcast;
        private static string pendingAbortReason;
        private static bool applyingCommittedRoster;
        private static ushort lastCommittedSequence;
        private static ulong lastCommittedRosterToken;
        private static ulong lastCommittedOwnerSteamId;
        private static ushort lastRejectedSequence;
        private static ulong lastRejectedRosterToken;
        private static ulong lastRejectedOwnerSteamId;
        private static bool lastRejectionWasCoordinatedLocally;
        private static float lastRejectionDeadline;
        private static float nextRejectionBroadcast;

        internal static bool HasPendingRoster
        {
            get
            {
                lock (Sync)
                {
                    return pendingRoster != null;
                }
            }
        }

        internal static bool IsApplyingCommittedRoster
        {
            get
            {
                lock (Sync)
                {
                    return applyingCommittedRoster;
                }
            }
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                ResetPendingLocked();
                lastCommittedSequence = 0;
                lastCommittedRosterToken = 0;
                lastCommittedOwnerSteamId = 0;
                ClearRejectedRosterLocked();
                applyingCommittedRoster = false;
            }
        }

        internal static bool BeginAsOwner(StartRoster roster, out string reason)
        {
            if (!RosterRuntime.TryValidateRoster(roster, (ulong)SteamClient.SteamId, out reason))
            {
                return false;
            }

            ulong rosterToken = PacketCodec.ComputeRosterToken(roster);
            lock (Sync)
            {
                if (RejectionIsActiveLocked())
                {
                    reason = "A rejected BoplEight roster transaction is still being disseminated.";
                    return false;
                }

                ClearRejectedRosterLocked();
                if (pendingRoster != null)
                {
                    if (localPlayerIsCoordinator
                        && MatchesPendingLocked(roster.Settings.SequenceNumber, rosterToken))
                    {
                        reason = null;
                        return true;
                    }

                    reason = "A different BoplEight roster transaction is already in progress.";
                    return false;
                }

                pendingRoster = roster;
                pendingRosterToken = rosterToken;
                pendingOwnerSteamId = (ulong)SteamClient.SteamId;
                localPlayerIsCoordinator = true;
                pendingNextLevel = BoplEightSession.ActiveRoster != null;
                commitWasSent = false;
                deadline = Time.unscaledTime + PreparationTimeoutSeconds;
                nextCommitBroadcast = 0f;
                pendingAbortReason = null;
                AcknowledgedPeers.Clear();
                CommitAcknowledgedPeers.Clear();
            }

            if (!RosterRuntime.BroadcastRoster(roster))
            {
                reason = "Steam rejected at least one BoplEight roster prepare message.";
                Abort(reason);
                return false;
            }

            reason = null;
            return true;
        }

        internal static bool PrepareRemote(StartRoster roster, ulong ownerSteamId, out string reason)
        {
            ulong rosterToken = PacketCodec.ComputeRosterToken(roster);
            if (!RosterRuntime.TryValidateRoster(roster, ownerSteamId, out reason))
            {
                SendControlTo(ownerSteamId, ModMessageType.RejectRoster, roster.Settings.SequenceNumber, rosterToken);
                return false;
            }

            lock (Sync)
            {
                if (RejectionIsActiveLocked())
                {
                    reason = "A rejected BoplEight roster transaction is still being disseminated.";
                }
                else if (pendingRoster != null
                    && (!MatchesPendingLocked(roster.Settings.SequenceNumber, rosterToken)
                        || localPlayerIsCoordinator
                        || pendingOwnerSteamId != ownerSteamId))
                {
                    reason = "A different BoplEight roster transaction is already in progress.";
                }
                else
                {
                    pendingRoster = roster;
                    pendingRosterToken = rosterToken;
                    pendingOwnerSteamId = ownerSteamId;
                    localPlayerIsCoordinator = false;
                    pendingNextLevel = BoplEightSession.ActiveRoster != null;
                    commitWasSent = false;
                    deadline = Time.unscaledTime + PreparationTimeoutSeconds;
                    nextCommitBroadcast = 0f;
                    pendingAbortReason = null;
                    AcknowledgedPeers.Clear();
                    CommitAcknowledgedPeers.Clear();
                    reason = null;
                }
            }

            if (reason != null)
            {
                SendControlTo(ownerSteamId, ModMessageType.RejectRoster, roster.Settings.SequenceNumber, rosterToken);
                return false;
            }

            if (!SendControlTo(ownerSteamId, ModMessageType.RosterAck, roster.Settings.SequenceNumber, rosterToken))
            {
                reason = "Steam rejected the BoplEight roster acknowledgement.";
                Abort(reason);
                return false;
            }

            return true;
        }

        internal static void ReceiveAcknowledgement(ushort sequenceNumber, ulong rosterToken, ulong senderSteamId)
        {
            if (!IsLivePeer(senderSteamId))
            {
                return;
            }

            bool shouldCommit;
            lock (Sync)
            {
                if (!localPlayerIsCoordinator
                    || commitWasSent
                    || pendingRoster == null
                    || !MatchesPendingLocked(sequenceNumber, rosterToken)
                    || RosterRuntime.FindBySteamId(pendingRoster, senderSteamId) == null
                    || senderSteamId == (ulong)SteamClient.SteamId)
                {
                    return;
                }

                AcknowledgedPeers.Add(senderSteamId);
                shouldCommit = AcknowledgedPeers.Count == pendingRoster.Players.Length - 1;
            }

            if (shouldCommit)
            {
                BeginCommitAsOwner();
            }
        }

        internal static void ReceiveCommit(ushort sequenceNumber, ulong rosterToken, ulong ownerSteamId)
        {
            StartRoster roster = null;
            bool duplicateCommit;
            bool rejectedCommit;
            lock (Sync)
            {
                duplicateCommit = pendingRoster == null
                    && sequenceNumber == lastCommittedSequence
                    && rosterToken == lastCommittedRosterToken
                    && ownerSteamId == lastCommittedOwnerSteamId;
                rejectedCommit = pendingRoster == null
                    && sequenceNumber == lastRejectedSequence
                    && rosterToken == lastRejectedRosterToken
                    && ownerSteamId == lastRejectedOwnerSteamId;
                if (!duplicateCommit && !rejectedCommit)
                {
                    if (localPlayerIsCoordinator
                        || pendingRoster == null
                        || pendingOwnerSteamId != ownerSteamId
                        || !MatchesPendingLocked(sequenceNumber, rosterToken))
                    {
                        return;
                    }

                    roster = pendingRoster;
                    commitWasSent = true;
                }
            }

            if (rejectedCommit)
            {
                SendControlTo(ownerSteamId, ModMessageType.RejectRoster, sequenceNumber, rosterToken);
                return;
            }

            if (duplicateCommit)
            {
                SendControlTo(ownerSteamId, ModMessageType.CommitAck, sequenceNumber, rosterToken);
                return;
            }

            string reason;
            if (!RosterRuntime.TryValidateRoster(roster, ownerSteamId, out reason)
                || !RosterRuntime.TryActivateRoster(roster, ownerSteamId, out reason))
            {
                Abort("Could not commit the prepared BoplEight roster: " + reason);
                return;
            }

            if (!TryApplyCommittedRoster(out reason))
            {
                Abort(reason);
                return;
            }

            RememberCommittedRoster(roster, rosterToken, ownerSteamId);
            SendControlTo(ownerSteamId, ModMessageType.CommitAck, sequenceNumber, rosterToken);
            ResetPendingOnly();
            BoplEightPlugin.Log.LogInfo("Accepted committed BoplEight roster sequence " + sequenceNumber + ".");
        }

        internal static void ReceiveCommitAcknowledgement(ushort sequenceNumber, ulong rosterToken, ulong senderSteamId)
        {
            if (!IsLivePeer(senderSteamId))
            {
                return;
            }

            bool shouldApply;
            lock (Sync)
            {
                if (!localPlayerIsCoordinator
                    || !commitWasSent
                    || pendingRoster == null
                    || !MatchesPendingLocked(sequenceNumber, rosterToken)
                    || RosterRuntime.FindBySteamId(pendingRoster, senderSteamId) == null
                    || senderSteamId == (ulong)SteamClient.SteamId)
                {
                    return;
                }

                CommitAcknowledgedPeers.Add(senderSteamId);
                shouldApply = CommitAcknowledgedPeers.Count == pendingRoster.Players.Length - 1;
            }

            if (shouldApply)
            {
                ApplyCommitAsOwner();
            }
        }

        internal static void ReceiveRejection(ushort sequenceNumber, ulong rosterToken, ulong senderSteamId)
        {
            bool rejectedCommittedRoster = false;
            lock (Sync)
            {
                if (pendingRoster == null)
                {
                    StartRoster activeRoster = BoplEightSession.ActiveRoster;
                    rejectedCommittedRoster = sequenceNumber == lastCommittedSequence
                        && rosterToken == lastCommittedRosterToken
                        && activeRoster != null
                        && RosterRuntime.FindBySteamId(activeRoster, senderSteamId) != null
                        && (lastCommittedOwnerSteamId == (ulong)SteamClient.SteamId
                            || senderSteamId == lastCommittedOwnerSteamId);
                }
                else
                {
                    if (!MatchesPendingLocked(sequenceNumber, rosterToken))
                    {
                        return;
                    }

                    if (localPlayerIsCoordinator)
                    {
                        if (senderSteamId == (ulong)SteamClient.SteamId
                            || RosterRuntime.FindBySteamId(pendingRoster, senderSteamId) == null)
                        {
                            return;
                        }
                    }
                    else if (senderSteamId != pendingOwnerSteamId)
                    {
                        return;
                    }
                }
            }

            if (rejectedCommittedRoster)
            {
                BoplEightSession.RequestMatchTermination("A peer rejected an already committed BoplEight roster.");
                return;
            }

            Abort("A peer rejected BoplEight roster sequence " + sequenceNumber + ".");
        }

        internal static void OnPeerDisconnected(ulong steamId)
        {
            lock (Sync)
            {
                if (pendingRoster != null && RosterRuntime.FindBySteamId(pendingRoster, steamId) != null)
                {
                    AcknowledgedPeers.Remove(steamId);
                    CommitAcknowledgedPeers.Remove(steamId);
                    pendingAbortReason = "BoplEight roster preparation stopped because a roster peer disconnected.";
                }
            }
        }

        internal static void Tick()
        {
            string abortReason = null;
            bool resendCommit = false;
            bool resendRejection = false;
            bool rejectionWasCoordinatedLocally = false;
            ushort sequenceNumber = 0;
            ulong rosterToken = 0;
            ulong expectedOwner = 0;
            ulong currentOwner = SteamManager.instance == null
                || !SteamClient.IsValid
                || (ulong)SteamManager.instance.currentLobby.Id == 0
                ? 0
                : (ulong)SteamManager.instance.currentLobby.Owner.Id;

            lock (Sync)
            {
                if (pendingRoster == null)
                {
                    if (lastRejectedRosterToken != 0 && Time.unscaledTime < lastRejectionDeadline)
                    {
                        if (Time.unscaledTime >= nextRejectionBroadcast)
                        {
                            resendRejection = true;
                            sequenceNumber = lastRejectedSequence;
                            rosterToken = lastRejectedRosterToken;
                            expectedOwner = lastRejectedOwnerSteamId;
                            rejectionWasCoordinatedLocally = lastRejectionWasCoordinatedLocally;
                            nextRejectionBroadcast = Time.unscaledTime + CommitRetrySeconds;
                        }
                    }
                    else
                    {
                        ClearRejectedRosterLocked();
                    }
                }
                else
                {
                    sequenceNumber = pendingRoster.Settings.SequenceNumber;
                    rosterToken = pendingRosterToken;
                    expectedOwner = pendingOwnerSteamId;
                    if (!string.IsNullOrEmpty(pendingAbortReason))
                    {
                        abortReason = pendingAbortReason;
                    }
                    else if (currentOwner == 0 || currentOwner != pendingOwnerSteamId)
                    {
                        abortReason = "BoplEight roster preparation stopped because lobby ownership changed.";
                    }
                    else if (Time.unscaledTime >= deadline)
                    {
                        abortReason = commitWasSent
                            ? "Timed out while waiting for every BoplEight peer to acknowledge the roster commit."
                            : "Timed out while waiting for every BoplEight peer to prepare the roster.";
                    }
                    else if (localPlayerIsCoordinator && commitWasSent && Time.unscaledTime >= nextCommitBroadcast)
                    {
                        resendCommit = true;
                        nextCommitBroadcast = Time.unscaledTime + CommitRetrySeconds;
                    }
                }
            }

            if (abortReason != null)
            {
                Abort(abortReason);
            }
            else if (resendCommit)
            {
                BroadcastControl(ModMessageType.CommitRoster, sequenceNumber, rosterToken);
            }
            else if (resendRejection)
            {
                if (rejectionWasCoordinatedLocally)
                {
                    BroadcastControl(ModMessageType.RejectRoster, sequenceNumber, rosterToken);
                }
                else
                {
                    SendControlTo(expectedOwner, ModMessageType.RejectRoster, sequenceNumber, rosterToken);
                }
            }
        }

        internal static void RestoreInitialStartState()
        {
            CharacterSelectHandler_online handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
            if (handler != null && IsStartingGameField != null)
            {
                IsStartingGameField.SetValue(handler, false);
            }
        }

        private static void BeginCommitAsOwner()
        {
            StartRoster roster;
            ulong rosterToken;
            lock (Sync)
            {
                if (!localPlayerIsCoordinator || commitWasSent || pendingRoster == null)
                {
                    return;
                }

                roster = pendingRoster;
                rosterToken = pendingRosterToken;
            }

            string reason = null;
            if (!SteamManager.LocalPlayerIsLobbyOwner
                || !RosterRuntime.TryValidateRoster(roster, (ulong)SteamClient.SteamId, out reason))
            {
                Abort("Could not finalize the prepared BoplEight roster: " + (reason ?? "the local player is no longer lobby owner."));
                return;
            }

            lock (Sync)
            {
                if (pendingRoster != roster || pendingRosterToken != rosterToken)
                {
                    return;
                }

                commitWasSent = true;
                deadline = Time.unscaledTime + CommitTimeoutSeconds;
                nextCommitBroadcast = Time.unscaledTime + CommitRetrySeconds;
                CommitAcknowledgedPeers.Clear();
            }

            BroadcastControl(ModMessageType.CommitRoster, roster.Settings.SequenceNumber, rosterToken);
        }

        private static void ApplyCommitAsOwner()
        {
            StartRoster roster;
            ulong rosterToken;
            lock (Sync)
            {
                if (!localPlayerIsCoordinator
                    || !commitWasSent
                    || pendingRoster == null
                    || CommitAcknowledgedPeers.Count != pendingRoster.Players.Length - 1)
                {
                    return;
                }

                roster = pendingRoster;
                rosterToken = pendingRosterToken;
            }

            string reason;
            if (!RosterRuntime.TryValidateRoster(roster, (ulong)SteamClient.SteamId, out reason)
                || !RosterRuntime.TryActivateRoster(roster, (ulong)SteamClient.SteamId, out reason))
            {
                Abort("Could not activate the committed BoplEight roster: " + reason);
                return;
            }

            if (!TryApplyCommittedRoster(out reason))
            {
                Abort(reason);
                return;
            }

            RememberCommittedRoster(roster, rosterToken, (ulong)SteamClient.SteamId);
            ResetPendingOnly();
            BoplEightPlugin.Log.LogInfo("Committed BoplEight roster sequence " + roster.Settings.SequenceNumber + ".");
        }

        private static bool TryApplyCommittedRoster(out string reason)
        {
            lock (Sync)
            {
                applyingCommittedRoster = true;
            }

            try
            {
                RosterRuntime.ApplyAcceptedRoster();
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                reason = "Failed while applying a committed BoplEight roster: " + exception;
                return false;
            }
            finally
            {
                lock (Sync)
                {
                    applyingCommittedRoster = false;
                }
            }
        }

        private static void Abort(string reason)
        {
            bool wasCoordinator;
            bool wasNextLevel;
            ushort sequenceNumber;
            ulong rosterToken;
            ulong ownerSteamId;
            bool commitHadBeenSent;
            lock (Sync)
            {
                if (pendingRoster == null)
                {
                    return;
                }

                wasCoordinator = localPlayerIsCoordinator;
                wasNextLevel = pendingNextLevel;
                sequenceNumber = pendingRoster.Settings.SequenceNumber;
                rosterToken = pendingRosterToken;
                ownerSteamId = pendingOwnerSteamId;
                commitHadBeenSent = commitWasSent;
                lastRejectedSequence = sequenceNumber;
                lastRejectedRosterToken = rosterToken;
                lastRejectedOwnerSteamId = ownerSteamId;
                lastRejectionWasCoordinatedLocally = wasCoordinator;
                lastRejectionDeadline = Time.unscaledTime + CommitTimeoutSeconds;
                nextRejectionBroadcast = Time.unscaledTime + CommitRetrySeconds;
                ResetPendingLocked();
            }

            if (wasCoordinator)
            {
                BroadcastControl(ModMessageType.RejectRoster, sequenceNumber, rosterToken);
            }
            else
            {
                SendControlTo(ownerSteamId, ModMessageType.RejectRoster, sequenceNumber, rosterToken);
            }

            BoplEightPlugin.Log.LogError(reason);
            if (commitHadBeenSent || wasNextLevel || BoplEightSession.ActiveRoster != null)
            {
                BoplEightSession.RequestMatchTermination(reason);
                return;
            }

            RestoreInitialStartState();
            if (SteamManager.instance != null && SteamManager.LocalPlayerIsLobbyOwner)
            {
                SteamManager.instance.currentLobby.SetJoinable(true);
            }
        }

        private static void RememberCommittedRoster(StartRoster roster, ulong rosterToken, ulong ownerSteamId)
        {
            lock (Sync)
            {
                lastCommittedSequence = roster.Settings.SequenceNumber;
                lastCommittedRosterToken = rosterToken;
                lastCommittedOwnerSteamId = ownerSteamId;
            }
        }

        private static void ResetPendingOnly()
        {
            lock (Sync)
            {
                ResetPendingLocked();
            }
        }

        private static void ResetPendingLocked()
        {
            pendingRoster = null;
            pendingRosterToken = 0;
            pendingOwnerSteamId = 0;
            localPlayerIsCoordinator = false;
            pendingNextLevel = false;
            commitWasSent = false;
            deadline = 0f;
            nextCommitBroadcast = 0f;
            pendingAbortReason = null;
            AcknowledgedPeers.Clear();
            CommitAcknowledgedPeers.Clear();
        }

        private static void ClearRejectedRosterLocked()
        {
            lastRejectedSequence = 0;
            lastRejectedRosterToken = 0;
            lastRejectedOwnerSteamId = 0;
            lastRejectionWasCoordinatedLocally = false;
            lastRejectionDeadline = 0f;
            nextRejectionBroadcast = 0f;
        }

        private static bool RejectionIsActiveLocked()
        {
            return lastRejectedRosterToken != 0 && Time.unscaledTime < lastRejectionDeadline;
        }

        private static bool MatchesPendingLocked(ushort sequenceNumber, ulong rosterToken)
        {
            return pendingRoster != null
                && pendingRoster.Settings.SequenceNumber == sequenceNumber
                && pendingRosterToken == rosterToken;
        }

        private static bool IsLivePeer(ulong steamId)
        {
            if (!BoplEightPlugin.IsCurrentLobbyMember(steamId))
            {
                return false;
            }

            SteamConnection connection = RosterRuntime.FindConnection(steamId);
            return connection != null && connection.Connected;
        }

        private static bool BroadcastControl(ModMessageType messageType, ushort sequenceNumber, ulong rosterToken)
        {
            if (SteamManager.instance == null)
            {
                return false;
            }

            bool allSent = true;
            byte[] packet = PacketCodec.EncodeRosterControl(messageType, sequenceNumber, rosterToken);
            for (var index = 0; index < SteamManager.instance.connectedPlayers.Count; index++)
            {
                SteamConnection connection = SteamManager.instance.connectedPlayers[index];
                if (!connection.Connected)
                {
                    allSent = false;
                    continue;
                }

                Result result = connection.Connection.SendMessage(packet, SendType.Reliable);
                if (result != Result.OK)
                {
                    allSent = false;
                    BoplEightPlugin.Log.LogWarning("Steam rejected a BoplEight " + messageType + " message for peer " + connection.id + ": " + result + ".");
                }
            }

            return allSent;
        }

        private static bool SendControlTo(ulong steamId, ModMessageType messageType, ushort sequenceNumber, ulong rosterToken)
        {
            SteamConnection connection = RosterRuntime.FindConnection(steamId);
            if (connection == null || !connection.Connected)
            {
                return false;
            }

            Result result = connection.Connection.SendMessage(
                PacketCodec.EncodeRosterControl(messageType, sequenceNumber, rosterToken),
                SendType.Reliable);
            if (result != Result.OK)
            {
                BoplEightPlugin.Log.LogWarning("Steam rejected a BoplEight " + messageType + " message for peer " + steamId + ": " + result + ".");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(SteamManager), "Update")]
    internal static class RosterStartCoordinatorUpdatePatch
    {
        private static void Postfix()
        {
            RosterStartCoordinator.Tick();
        }
    }
}
