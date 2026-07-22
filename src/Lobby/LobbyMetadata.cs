using System;
using System.Collections.Generic;
using System.Globalization;

namespace BoplEight.Lobby
{
    public sealed class ModIdentity
    {
        public ModIdentity(string pluginId, string pluginVersion, byte protocolVersion, string gameAssemblyHash)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin ID is required.", "pluginId");
            }

            if (string.IsNullOrWhiteSpace(pluginVersion))
            {
                throw new ArgumentException("Plugin version is required.", "pluginVersion");
            }

            if (string.IsNullOrWhiteSpace(gameAssemblyHash))
            {
                throw new ArgumentException("Game assembly hash is required.", "gameAssemblyHash");
            }

            PluginId = pluginId;
            PluginVersion = pluginVersion;
            ProtocolVersion = protocolVersion;
            GameAssemblyHash = gameAssemblyHash;
        }

        public string PluginId { get; private set; }

        public string PluginVersion { get; private set; }

        public byte ProtocolVersion { get; private set; }

        public string GameAssemblyHash { get; private set; }
    }

    public static class LobbyMetadata
    {
        public const string PluginIdKey = "BoplEight.PluginId";
        public const string PluginVersionKey = "BoplEight.PluginVersion";
        public const string ProtocolVersionKey = "BoplEight.ProtocolVersion";
        public const string GameAssemblyHashKey = "BoplEight.GameAssemblyHash";
        public const string MinimumPlayersKey = "BoplEight.MinimumPlayers";
        public const string MaximumPlayersKey = "BoplEight.MaximumPlayers";

        public static IDictionary<string, string> Create(ModIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException("identity");
            }

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { PluginIdKey, identity.PluginId },
                { PluginVersionKey, identity.PluginVersion },
                { ProtocolVersionKey, identity.ProtocolVersion.ToString(CultureInfo.InvariantCulture) },
                { GameAssemblyHashKey, identity.GameAssemblyHash },
                { MinimumPlayersKey, Protocol.ProtocolConstants.MinimumPlayers.ToString(CultureInfo.InvariantCulture) },
                { MaximumPlayersKey, Protocol.ProtocolConstants.MaximumPlayers.ToString(CultureInfo.InvariantCulture) }
            };
        }

        public static bool TryValidate(IDictionary<string, string> metadata, ModIdentity localIdentity, int requestedPlayerCount, out string reason)
        {
            if (requestedPlayerCount < Protocol.ProtocolConstants.MinimumPlayers || requestedPlayerCount > Protocol.ProtocolConstants.MaximumPlayers)
            {
                reason = "BoplEight supports between two and eight players.";
                return false;
            }

            return TryValidateForJoin(metadata, localIdentity, out reason);
        }

        public static bool TryValidateForJoin(IDictionary<string, string> metadata, ModIdentity localIdentity, out string reason)
        {
            reason = null;
            if (metadata == null)
            {
                reason = "The lobby did not publish BoplEight compatibility data.";
                return false;
            }

            if (localIdentity == null)
            {
                reason = "The local BoplEight identity is unavailable.";
                return false;
            }

            string value;
            if (!metadata.TryGetValue(PluginIdKey, out value) || !string.Equals(value, localIdentity.PluginId, StringComparison.Ordinal))
            {
                reason = "The lobby uses a different BoplEight plugin ID.";
                return false;
            }

            if (!metadata.TryGetValue(PluginVersionKey, out value) || !string.Equals(value, localIdentity.PluginVersion, StringComparison.Ordinal))
            {
                reason = "The lobby uses a different BoplEight plugin version.";
                return false;
            }

            if (!metadata.TryGetValue(ProtocolVersionKey, out value) || value != localIdentity.ProtocolVersion.ToString(CultureInfo.InvariantCulture))
            {
                reason = "The lobby uses a different BoplEight network protocol.";
                return false;
            }

            if (!metadata.TryGetValue(GameAssemblyHashKey, out value) || !string.Equals(value, localIdentity.GameAssemblyHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The lobby uses a different game build.";
                return false;
            }

            int ignoredMinimumPlayers;
            int ignoredMaximumPlayers;
            if (!TryReadPlayerRange(metadata, out ignoredMinimumPlayers, out ignoredMaximumPlayers))
            {
                reason = "The lobby advertised an invalid BoplEight player range.";
                return false;
            }

            return true;
        }

        private static bool TryReadPlayerRange(IDictionary<string, string> metadata, out int minimumPlayers, out int maximumPlayers)
        {
            minimumPlayers = 0;
            maximumPlayers = 0;

            string minimumValue;
            string maximumValue;
            if (!metadata.TryGetValue(MinimumPlayersKey, out minimumValue) || !metadata.TryGetValue(MaximumPlayersKey, out maximumValue))
            {
                return false;
            }

            if (!int.TryParse(minimumValue, NumberStyles.None, CultureInfo.InvariantCulture, out minimumPlayers)
                || !int.TryParse(maximumValue, NumberStyles.None, CultureInfo.InvariantCulture, out maximumPlayers))
            {
                return false;
            }

            return minimumPlayers == Protocol.ProtocolConstants.MinimumPlayers
                && maximumPlayers == Protocol.ProtocolConstants.MaximumPlayers;
        }
    }
}
