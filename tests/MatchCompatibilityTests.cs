using System;
using System.Collections.Generic;
using BoplEight.Lobby;

namespace BoplEight.Tests
{
    internal static class MatchCompatibilityTests
    {
        public static void LobbiesAcceptTwoThroughEightPlayers()
        {
            ModIdentity identity = NewIdentity("1.0.0", "game-hash-a");
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
            IDictionary<string, string> metadata = LobbyMetadata.Create(NewIdentity("1.0.0", "game-hash-a"));
            string reason;

            bool accepted = LobbyMetadata.TryValidate(metadata, NewIdentity("1.0.1", "game-hash-a"), 4, out reason);

            Assert.False(accepted, "Different plugin versions must not enter the same deterministic match.");
            Assert.Contains("plugin version", reason, "The rejection should identify the incompatible plugin version.");
        }

        public static void LobbiesAllowAHostToFormAMatchBeforeASecondPlayerJoins()
        {
            ModIdentity identity = NewIdentity("1.0.0", "game-hash-a");
            IDictionary<string, string> metadata = LobbyMetadata.Create(identity);
            string reason;

            bool accepted = LobbyMetadata.TryValidateForJoin(metadata, identity, out reason);

            Assert.True(accepted, "A host must be able to stay in a compatible lobby while waiting for the second player. Reason: " + reason);
        }

        public static void LobbiesRejectMismatchedGameAssemblies()
        {
            IDictionary<string, string> metadata = LobbyMetadata.Create(NewIdentity("1.0.0", "game-hash-a"));
            string reason;

            bool accepted = LobbyMetadata.TryValidate(metadata, NewIdentity("1.0.0", "game-hash-b"), 4, out reason);

            Assert.False(accepted, "Different game assemblies must not enter the same deterministic match.");
            Assert.Contains("game build", reason, "The rejection should identify the incompatible game build.");
        }

        private static ModIdentity NewIdentity(string pluginVersion, string gameAssemblyHash)
        {
            return new ModIdentity("io.opencode.bopleight", pluginVersion, Protocol.ProtocolConstants.Version, gameAssemblyHash);
        }
    }
}
