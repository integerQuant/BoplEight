using System;
using System.Collections.Generic;
using BoplEight.Lobby;

namespace BoplEight.Tests
{
    internal static class MatchCompatibilityTests
    {
        public static void LobbiesAcceptTwoThroughEightPlayers()
        {
            ModIdentity identity = NewIdentity("1.0.1", "game-hash-a");
            IDictionary<string, string> metadata = LobbyMetadata.Create(identity);

            for (var playerCount = 2; playerCount <= 8; playerCount++)
            {
                string reason;
                Assert.True(
                    LobbyMetadata.TryValidate(metadata, identity, playerCount, out reason),
                    "A matching modded lobby should allow " + playerCount + " players. Reason: " + reason);
            }
        }

        public static void LobbiesRejectMismatchedVersions()
        {
            IDictionary<string, string> metadata = LobbyMetadata.Create(NewIdentity("1.0.1", "game-hash-a"));
            string reason;

            bool accepted = LobbyMetadata.TryValidate(metadata, NewIdentity("1.0.2", "game-hash-a"), 4, out reason);

            Assert.False(accepted, "Different plugin versions must not enter the same deterministic match.");
            Assert.Contains("plugin version", reason, "The rejection should identify the incompatible plugin version.");
        }

        public static void LobbiesAllowAHostToFormAMatchBeforeASecondPlayerJoins()
        {
            ModIdentity identity = NewIdentity("1.0.1", "game-hash-a");
            IDictionary<string, string> metadata = LobbyMetadata.Create(identity);
            string reason;

            bool accepted = LobbyMetadata.TryValidateForJoin(metadata, identity, out reason);

            Assert.True(accepted, "A host must be able to stay in a compatible lobby while waiting for the second player. Reason: " + reason);
        }

        public static void LobbyInvitesAwaitCompatibilityMetadataBeforeRejecting()
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            string reason;

            LobbyJoinDecision decision = LobbyMetadata.EvaluateForJoin(
                metadata,
                NewIdentity("1.0.1", "game-hash-a"),
                false,
                out reason);

            Assert.Equal(LobbyJoinDecision.AwaitMetadata, decision, "An uncached Steam invite must wait for lobby metadata.");
            Assert.Equal<string>(null, reason, "Waiting for Steam metadata must not display a rejection error.");
        }

        public static void RefreshedVanillaLobbyInvitesAreRejected()
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            string reason;

            LobbyJoinDecision decision = LobbyMetadata.EvaluateForJoin(
                metadata,
                NewIdentity("1.0.1", "game-hash-a"),
                true,
                out reason);

            Assert.Equal(LobbyJoinDecision.Reject, decision, "A refreshed vanilla lobby must remain isolated from BoplEight.");
            Assert.Contains("does not have BoplEight installed", reason, "The rejection should identify a vanilla lobby.");
        }

        public static void CachedCompatibleLobbyInvitesJoinWithoutRefreshing()
        {
            ModIdentity identity = NewIdentity("1.0.1", "game-hash-a");
            string reason;

            LobbyJoinDecision decision = LobbyMetadata.EvaluateForJoin(
                LobbyMetadata.Create(identity),
                identity,
                false,
                out reason);

            Assert.Equal(LobbyJoinDecision.Accept, decision, "Already cached compatible metadata should permit the join immediately.");
            Assert.Equal<string>(null, reason, "A compatible lobby should not report an error.");
        }

        public static void NewLobbyJoinRequestsReplaceStalePendingRequests()
        {
            var pending = new PendingLobbyJoinState();
            pending.Begin(101, 10f, 5f);

            pending.Begin(202, 11f, 5f);

            Assert.False(pending.TryComplete(101), "A stale Steam callback must not resume a replaced lobby join.");
            Assert.True(pending.TryComplete(202), "The newest lobby callback should complete its pending join.");
            Assert.False(pending.TryComplete(202), "A completed lobby callback must not resume more than once.");
        }

        public static void LobbyJoinTimeoutsAreConsumedOnce()
        {
            var pending = new PendingLobbyJoinState();
            pending.Begin(303, 20f, 5f);

            Assert.False(pending.TryTakeTimeout(24.99f), "A pending lobby should not time out before its deadline.");
            Assert.True(pending.TryTakeTimeout(25f), "A pending lobby should time out at its deadline.");
            Assert.False(pending.TryTakeTimeout(30f), "A lobby timeout must be consumed exactly once.");
        }

        public static void PendingLobbyJoinCancellationIsConsumedOnce()
        {
            var pending = new PendingLobbyJoinState();
            Assert.False(pending.IsActive, "A new pending-join state should not block Steam joins.");
            Assert.False(pending.TryCancel(), "An inactive pending join must not claim the game's join reservation.");

            pending.Begin(404, 30f, 5f);

            Assert.True(pending.IsActive, "An active metadata request should block competing Steam joins.");
            Assert.True(pending.TryCancel(), "An active pending join should release its join reservation.");
            Assert.False(pending.IsActive, "A cancelled metadata request should stop blocking Steam joins.");
            Assert.False(pending.TryCancel(), "A released join reservation must not be released again.");
        }

        public static void SteamLobbyJoinGateAllowsOnlyOneActiveJoin()
        {
            var gate = new LobbyJoinGate();

            Assert.True(gate.TryEnter(), "The first Steam lobby join should acquire the gate.");
            Assert.False(gate.TryEnter(), "A concurrent Steam lobby join must be rejected.");

            gate.Exit();

            Assert.True(gate.TryEnter(), "A completed Steam lobby join should release the gate for the next request.");
            gate.Exit();
        }

        public static void LobbiesRejectMismatchedGameAssemblies()
        {
            IDictionary<string, string> metadata = LobbyMetadata.Create(NewIdentity("1.0.1", "game-hash-a"));
            string reason;

            bool accepted = LobbyMetadata.TryValidate(metadata, NewIdentity("1.0.1", "game-hash-b"), 4, out reason);

            Assert.False(accepted, "Different game assemblies must not enter the same deterministic match.");
            Assert.Contains("game build", reason, "The rejection should identify the incompatible game build.");
        }

        private static ModIdentity NewIdentity(string pluginVersion, string gameAssemblyHash)
        {
            return new ModIdentity("io.opencode.bopleight", pluginVersion, Protocol.ProtocolConstants.Version, gameAssemblyHash);
        }
    }
}
