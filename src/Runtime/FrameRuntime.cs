using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BoplEight.Protocol;
using HarmonyLib;
using Steamworks.Data;
using UnityEngine;

namespace BoplEight.Runtime
{
    internal sealed class RosterInputFrame
    {
        private readonly InputPacket[] inputs = new InputPacket[ProtocolConstants.MaximumPlayers];
        private readonly bool[] populated = new bool[ProtocolConstants.MaximumPlayers];

        internal void Set(int playerId, InputPacket input)
        {
            if (playerId < 1 || playerId > ProtocolConstants.MaximumPlayers)
            {
                throw new ArgumentOutOfRangeException("playerId", "BoplEight player IDs must be between 1 and 8.");
            }

            inputs[playerId - 1] = input;
            populated[playerId - 1] = true;
        }

        internal InputPacket GetOrDefault(int playerId)
        {
            if (playerId < 1 || playerId > ProtocolConstants.MaximumPlayers)
            {
                return default(InputPacket);
            }

            return inputs[playerId - 1];
        }

        internal bool HasInput(int playerId)
        {
            return playerId >= 1
                && playerId <= ProtocolConstants.MaximumPlayers
                && populated[playerId - 1];
        }

        internal InputPacketQuad ToVanillaQuad()
        {
            var quad = new InputPacketQuad
            {
                p1 = inputs[0],
                p2 = inputs[1],
                p3 = inputs[2],
                p4 = inputs[3]
            };

            uint maximumSequence = 0;
            bool hasSequence = false;
            byte maximumTargetDelay = 0;
            for (var index = 0; index < inputs.Length; index++)
            {
                if (!populated[index])
                {
                    continue;
                }

                if (!hasSequence
                    || (inputs[index].seqNumber != maximumSequence
                        && inputs[index].seqNumber - maximumSequence < 0x80000000u))
                {
                    maximumSequence = inputs[index].seqNumber;
                    hasSequence = true;
                }
                maximumTargetDelay = Math.Max(maximumTargetDelay, inputs[index].targetDelayBufferSize);
            }

            // Host.Update only reads these fields to adapt its shared delay buffer. The real p1
            // input is restored from this frame immediately before the deterministic tick.
            quad.p1.seqNumber = maximumSequence;
            quad.p2.seqNumber = maximumSequence;
            quad.p3.seqNumber = maximumSequence;
            quad.p4.seqNumber = maximumSequence;
            quad.p1.targetDelayBufferSize = maximumTargetDelay;
            return quad;
        }
    }

    internal static class FrameRuntime
    {
        private static readonly object Sync = new object();
        private static readonly Queue<RosterInputFrame> InputFrames = new Queue<RosterInputFrame>();

        internal static void Reset()
        {
            lock (Sync)
            {
                InputFrames.Clear();
            }
        }

        internal static bool TryDispatchGameplayPacket(NetIdentity sender, IntPtr data, int size)
        {
            if (size != NetworkTools.ackByteSize
                && size != NetworkTools.checkSumSize
                && size != NetworkTools.inputPacketUpdateByteSizeNoWASD
                && size != NetworkTools.inputPacketUpdateByteSizeWASD)
            {
                return false;
            }

            StartRoster roster = BoplEightSession.ActiveRoster;
            Host host = SteamManager.networkClientHandle;
            if (roster == null || host == null || !host.hasBeenInitialized || !sender.IsSteamId)
            {
                return false;
            }

            ulong senderSteamId = (ulong)sender.SteamId;
            PlayerDescriptor descriptor = RosterRuntime.FindBySteamId(roster, senderSteamId);
            if (descriptor == null || !BoplEightPlugin.IsCurrentLobbyMember(senderSteamId))
            {
                BoplEightPlugin.Log.LogWarning("Rejected a gameplay packet from a Steam connection outside the active roster.");
                return true;
            }

            var packet = new byte[size];
            Marshal.Copy(data, packet, 0, packet.Length);
            byte expectedPlayerId = (byte)(descriptor.Slot + 1);
            if (packet[0] != expectedPlayerId)
            {
                BoplEightPlugin.Log.LogWarning("Rejected a gameplay packet whose player ID did not match its Steam sender.");
                return true;
            }

            host.ReadPacket(packet, packet.Length);
            return true;
        }

        private static void Enqueue(RosterInputFrame frame, Queue<InputPacketQuad> vanillaInputBuffer)
        {
            lock (Sync)
            {
                InputFrames.Enqueue(frame);
            }

            vanillaInputBuffer.Enqueue(frame.ToVanillaQuad());
        }

        private static bool TryDequeue(out RosterInputFrame frame)
        {
            lock (Sync)
            {
                if (InputFrames.Count == 0)
                {
                    frame = null;
                    return false;
                }

                frame = InputFrames.Dequeue();
                return true;
            }
        }

        [HarmonyPatch(typeof(Host), "Init")]
        private static class HostInitPatch
        {
            private static void Postfix()
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    Reset();
                }
            }
        }

        [HarmonyPatch(typeof(Host), "ProcessNetworkPackets")]
        private static class HostProcessNetworkPacketsPatch
        {
            private static bool Prefix(
                Host __instance,
                List<Client> ___clients,
                TimedInputPacket[] ___inputHistory,
                int ___localPlayerId,
                Queue<InputPacket> ___sentPacketHistory,
                Queue<InputPacketQuad> ___InputBuffer,
                ref bool ___allClientsSynced,
                ref int ___UpdatesPlacedInDelayBuffer,
                ref float ___timeSinceLastBroadcast)
            {
                if (BoplEightSession.ActiveRoster == null)
                {
                    return true;
                }

                bool clientsAreSynchronized = true;
                for (var index = 0; index < ___clients.Count; index++)
                {
                    Client client = ___clients[index];
                    client.ProcessNetworkPackets(
                        Time.unscaledTime,
                        ___inputHistory,
                        __instance.AdaptiveInputDelayBuffer,
                        (byte)___localPlayerId);
                    clientsAreSynchronized &= client.HasReceivedSyncAck;
                    clientsAreSynchronized &= client.HasSentSyncAck;
                }

                if (clientsAreSynchronized && !___allClientsSynced)
                {
                    ___allClientsSynced = true;
                    for (var index = 0; index < Host.CurrentDelayBufferSize; index++)
                    {
                        Enqueue(new RosterInputFrame(), ___InputBuffer);
                    }
                }

                if (!___allClientsSynced)
                {
                    return false;
                }

                uint placedSequence = unchecked((uint)___UpdatesPlacedInDelayBuffer);
                uint framesReadyUnsigned = __instance.inputPacketsSent - placedSequence;
                for (var index = 0; index < ___clients.Count; index++)
                {
                    framesReadyUnsigned = Math.Min(
                        framesReadyUnsigned,
                        ___clients[index].InputPacketsReceived - placedSequence);
                }

                if (framesReadyUnsigned > int.MaxValue)
                {
                    RequestHostTermination(
                        __instance,
                        ref ___allClientsSynced,
                        ref ___timeSinceLastBroadcast,
                        "BoplEight detected invalid wrapped input counters; ending the match.");
                    return false;
                }

                int framesReady = (int)framesReadyUnsigned;
                for (var frameIndex = 0; frameIndex < framesReady; frameIndex++)
                {
                    var frame = new RosterInputFrame();
                    for (var clientIndex = 0; clientIndex < ___clients.Count; clientIndex++)
                    {
                        Client client = ___clients[clientIndex];
                        if (client.inputHistory.Count == 0)
                        {
                            RequestHostTermination(
                                __instance,
                                ref ___allClientsSynced,
                                ref ___timeSinceLastBroadcast,
                                "A synchronized BoplEight client had no queued input frame; ending the match.");
                            return false;
                        }

                        frame.Set(client.PlayerId, client.inputHistory.Dequeue());
                    }

                    if (___sentPacketHistory.Count == 0)
                    {
                        RequestHostTermination(
                            __instance,
                            ref ___allClientsSynced,
                            ref ___timeSinceLastBroadcast,
                            "BoplEight's local input history was empty during frame aggregation; ending the match.");
                        return false;
                    }

                    frame.Set(___localPlayerId, ___sentPacketHistory.Dequeue());
                    StartRoster activeRoster = BoplEightSession.ActiveRoster;
                    for (var rosterIndex = 0; rosterIndex < activeRoster.Players.Length; rosterIndex++)
                    {
                        int playerId = activeRoster.Players[rosterIndex].Slot + 1;
                        if (!frame.HasInput(playerId))
                        {
                            RequestHostTermination(
                                __instance,
                                ref ___allClientsSynced,
                                ref ___timeSinceLastBroadcast,
                                "BoplEight refused to advance an incomplete roster frame; ending the match.");
                            return false;
                        }
                    }

                    Enqueue(frame, ___InputBuffer);
                    ___UpdatesPlacedInDelayBuffer++;
                }

                try
                {
                    if (___localPlayerId == 1)
                    {
                        for (var index = 0; index < ___clients.Count; index++)
                        {
                            ___clients[index].CheckForDesyncs();
                        }
                    }
                }
                catch (Exception exception)
                {
                    BoplEightSession.RequestMatchTermination("Desync occurred, ending BoplEight game: " + exception);
                }

                return false;
            }
        }


        [HarmonyPatch(typeof(Host), "Update")]
        private static class HostActiveRosterGuardPatch
        {
            private static bool Prefix(Host __instance)
            {
                StartRoster roster = BoplEightSession.ActiveRoster;
                if (roster == null || !__instance.hasBeenInitialized)
                {
                    return true;
                }

                PlayerDescriptor authority = RosterRuntime.FindByPlayerId(roster, 1);
                if (authority == null
                    || SteamManager.instance == null
                    || authority.SteamId != (ulong)SteamManager.instance.currentLobby.Owner.Id)
                {
                    __instance.hasBeenInitialized = false;
                    BoplEightSession.RequestMatchTermination("BoplEight ended the match because player-one authority changed.");
                    return false;
                }

                ulong localSteamId = (ulong)Steamworks.SteamClient.SteamId;
                for (var index = 0; index < roster.Players.Length; index++)
                {
                    PlayerDescriptor player = roster.Players[index];
                    if (player.SteamId == localSteamId)
                    {
                        continue;
                    }

                    SteamConnection connection = RosterRuntime.FindConnection(player.SteamId);
                    if (connection == null || !connection.Connected)
                    {
                        __instance.hasBeenInitialized = false;
                        BoplEightSession.RequestMatchTermination("BoplEight ended the match after losing roster slot " + (player.Slot + 1) + ".");
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Updater), "TickSimulation")]
        private static class UpdaterTickSimulationPatch
        {
            private static void Prefix()
            {
                if (BoplEightSession.ActiveRoster == null || !GameLobby.isOnlineGame || GameLobby.isPlayingAReplay)
                {
                    return;
                }

                RosterInputFrame frame;
                if (!TryDequeue(out frame))
                {
                    const string reason = "BoplEight's frame queue fell out of sync with Host.InputBuffer.";
                    BoplEightSession.RequestMatchTermination(reason);
                    throw new InvalidOperationException(reason);
                }

                List<Player> players = PlayerHandler.Get().PlayerList();
                for (var index = 0; index < players.Count; index++)
                {
                    players[index].OverrideInputWithNetworkInput(frame.GetOrDefault(players[index].Id));
                }
            }
        }

        private static void RequestHostTermination(
            Host host,
            ref bool allClientsSynced,
            ref float timeSinceLastBroadcast,
            string reason)
        {
            host.hasBeenInitialized = false;
            allClientsSynced = false;
            timeSinceLastBroadcast = 0f;
            BoplEightSession.RequestMatchTermination(reason);
        }
    }
}
