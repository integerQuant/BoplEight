using System.Collections.Generic;
using BoplEight.Protocol;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace BoplEight.Runtime
{
    internal static class PeerCompatibility
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<ulong> AcknowledgedPeers = new HashSet<ulong>();
        private static readonly Dictionary<ulong, float> LastHelloSent = new Dictionary<ulong, float>();

        internal static void Reset()
        {
            lock (Sync)
            {
                AcknowledgedPeers.Clear();
                LastHelloSent.Clear();
            }
        }

        internal static void OnHello(ulong senderSteamId, ulong manifestToken)
        {
            if (!BoplEightPlugin.IsCurrentLobbyMember(senderSteamId))
            {
                return;
            }

            ulong localManifest = AssetManifest.CurrentToken();
            if (localManifest == 0 || manifestToken != localManifest)
            {
                RemovePeer(senderSteamId);
                BoplEightPlugin.Log.LogWarning("Rejected BoplEight peer " + senderSteamId + " because its deterministic asset manifest differs.");
                return;
            }

            SteamConnection connection = RosterRuntime.FindConnection(senderSteamId);
            if (connection != null && connection.Connected)
            {
                Result result = connection.Connection.SendMessage(
                    PacketCodec.EncodePeerHandshake(ModMessageType.HelloAck, localManifest),
                    SendType.Reliable);
                if (result != Result.OK)
                {
                    BoplEightPlugin.Log.LogWarning("Steam rejected a BoplEight peer acknowledgement for " + senderSteamId + ": " + result + ".");
                }
            }
        }

        internal static void OnHelloAcknowledged(ulong senderSteamId, ulong manifestToken)
        {
            if (!BoplEightPlugin.IsCurrentLobbyMember(senderSteamId))
            {
                return;
            }

            ulong localManifest = AssetManifest.CurrentToken();
            if (localManifest == 0 || manifestToken != localManifest)
            {
                RemovePeer(senderSteamId);
                return;
            }

            lock (Sync)
            {
                if (AcknowledgedPeers.Add(senderSteamId))
                {
                    BoplEightPlugin.Log.LogInfo("Verified BoplEight peer " + senderSteamId + ".");
                }
            }
        }

        internal static void RemovePeer(ulong steamId)
        {
            lock (Sync)
            {
                AcknowledgedPeers.Remove(steamId);
                LastHelloSent.Remove(steamId);
            }
        }

        internal static bool AllConnectedPeersAreCompatible(out string reason)
        {
            if (SteamManager.instance == null)
            {
                reason = "The Steam lobby is unavailable.";
                return false;
            }

            lock (Sync)
            {
                for (var index = 0; index < SteamManager.instance.connectedPlayers.Count; index++)
                {
                    SteamConnection connection = SteamManager.instance.connectedPlayers[index];
                    if (!connection.Connected || !AcknowledgedPeers.Contains((ulong)connection.id))
                    {
                        reason = "Waiting for " + connection.steamName + " to load the matching BoplEight plugin.";
                        return false;
                    }
                }
            }

            reason = null;
            return true;
        }

        private static void SendPendingHellos(SteamManager manager)
        {
            if (manager == null
                || !Steamworks.SteamClient.IsValid
                || (ulong)manager.currentLobby.Id == 0
                || BoplEightSession.ActiveRoster != null
                || !GameSession.inMenus
                || !BoplEightPlugin.IsBoplEightLobby(manager.currentLobby))
            {
                return;
            }

            ulong manifestToken = AssetManifest.CurrentToken();
            if (manifestToken == 0)
            {
                return;
            }

            var pendingConnections = new List<SteamConnection>();
            lock (Sync)
            {
                for (var index = 0; index < manager.connectedPlayers.Count; index++)
                {
                    SteamConnection connection = manager.connectedPlayers[index];
                    ulong steamId = (ulong)connection.id;
                    if (!connection.Connected || AcknowledgedPeers.Contains(steamId))
                    {
                        continue;
                    }

                    float lastSent;
                    if (!LastHelloSent.TryGetValue(steamId, out lastSent) || Time.unscaledTime - lastSent >= 1f)
                    {
                        LastHelloSent[steamId] = Time.unscaledTime;
                        pendingConnections.Add(connection);
                    }
                }
            }

            byte[] helloPacket = PacketCodec.EncodePeerHandshake(ModMessageType.Hello, manifestToken);
            for (var index = 0; index < pendingConnections.Count; index++)
            {
                SteamConnection connection = pendingConnections[index];
                Result result = connection.Connection.SendMessage(helloPacket, SendType.Reliable);
                if (result != Result.OK)
                {
                    BoplEightPlugin.Log.LogWarning("Steam rejected a BoplEight peer hello for " + connection.id + ": " + result + ".");
                }
            }
        }

        [HarmonyPatch(typeof(SteamManager), "Update")]
        private static class SteamManagerUpdatePatch
        {
            private static void Postfix(SteamManager __instance)
            {
                SendPendingHellos(__instance);
            }
        }

        [HarmonyPatch(typeof(CharacterSelectHandler_online), "Update")]
        private static class CharacterSelectUpdatePatch
        {
            private static void Postfix()
            {
                string ignoredReason;
                if (!AllConnectedPeersAreCompatible(out ignoredReason))
                {
                    CharacterSelectHandler_online.startButtonAvailable = false;
                }
            }
        }
    }


    [HarmonyPatch(typeof(SteamConnection), "OnDisconnected")]
    internal static class SteamConnectionCompatibilityDisconnectPatch
    {
        private static void Prefix(SteamConnection __instance)
        {
            PeerCompatibility.RemovePeer((ulong)__instance.id);
            RosterStartCoordinator.OnPeerDisconnected((ulong)__instance.id);
        }
    }
}
