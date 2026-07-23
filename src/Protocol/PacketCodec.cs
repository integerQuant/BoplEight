using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BoplEight.Protocol
{
    public static class ProtocolConstants
    {
        public const ushort Magic = 0x3845;
        public const byte Version = 3;
        public const byte MinimumPlayers = 2;
        public const byte MaximumPlayers = 8;
        public const byte PlayerColorCount = 12;
        public const byte AbilityCount = 3;
        public const ulong CompatibilityToken = 0x42504C3845494748UL;
    }

    public enum ModMessageType : byte
    {
        Hello = 112,
        HelloAck = 113,
        StartRoster = 114,
        RosterAck = 116,
        CommitRoster = 117,
        RejectRoster = 118,
        CommitAck = 119
    }

    public sealed class ProtocolException : IOException
    {
        public ProtocolException(string message) : base(message)
        {
        }
    }

    public sealed class PlayerDescriptor
    {
        public PlayerDescriptor(
            byte slot,
            ulong steamId,
            byte playerColorId,
            byte teamId,
            bool usesKeyboardAndMouse,
            byte[] abilityIds)
        {
            if (slot >= ProtocolConstants.MaximumPlayers)
            {
                throw new ArgumentException("Player slots must be between 0 and 7.", "slot");
            }

            if (steamId == 0)
            {
                throw new ArgumentException("Steam ID must not be zero.", "steamId");
            }

            if (playerColorId >= ProtocolConstants.PlayerColorCount)
            {
                throw new ArgumentException("Player color IDs must be between 0 and 11.", "playerColorId");
            }

            if (teamId >= ProtocolConstants.MaximumPlayers)
            {
                throw new ArgumentException("Team IDs must be between 0 and 7.", "teamId");
            }

            if (abilityIds == null || abilityIds.Length != ProtocolConstants.AbilityCount)
            {
                throw new ArgumentException("Each player must have exactly three selected abilities.", "abilityIds");
            }

            Slot = slot;
            SteamId = steamId;
            PlayerColorId = playerColorId;
            TeamId = teamId;
            UsesKeyboardAndMouse = usesKeyboardAndMouse;
            AbilityIds = (byte[])abilityIds.Clone();
        }

        public byte Slot { get; private set; }

        public ulong SteamId { get; private set; }

        public byte PlayerColorId { get; private set; }

        public byte TeamId { get; private set; }

        public bool UsesKeyboardAndMouse { get; private set; }

        public byte[] AbilityIds { get; private set; }
    }

    public sealed class MatchStartSettings
    {
        public MatchStartSettings(ushort sequenceNumber, uint seed, byte abilityCount, byte level, byte frameBufferSize, byte demoMask)
        {
            if (abilityCount == 0 || abilityCount > ProtocolConstants.AbilityCount)
            {
                throw new ArgumentException("Ability count must be between one and three.", "abilityCount");
            }

            SequenceNumber = sequenceNumber;
            Seed = seed;
            AbilityCount = abilityCount;
            Level = level;
            FrameBufferSize = frameBufferSize;
            DemoMask = demoMask;
        }

        public ushort SequenceNumber { get; private set; }

        public uint Seed { get; private set; }

        public byte AbilityCount { get; private set; }

        public byte Level { get; private set; }

        public byte FrameBufferSize { get; private set; }

        public byte DemoMask { get; private set; }
    }

    public sealed class StartRoster
    {
        public StartRoster(MatchStartSettings settings, PlayerDescriptor[] players)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (players == null)
            {
                throw new ArgumentNullException("players");
            }

            if (players.Length < ProtocolConstants.MinimumPlayers || players.Length > ProtocolConstants.MaximumPlayers)
            {
                throw new ArgumentException("A modded roster must contain between two and eight players.", "players");
            }

            var slots = new HashSet<byte>();
            var steamIds = new HashSet<ulong>();
            for (var index = 0; index < players.Length; index++)
            {
                PlayerDescriptor player = players[index];
                if (player == null)
                {
                    throw new ArgumentException("A roster cannot contain a null player.", "players");
                }

                if (!slots.Add(player.Slot))
                {
                    throw new ArgumentException("A roster cannot contain duplicate player slots.", "players");
                }

                if (!steamIds.Add(player.SteamId))
                {
                    throw new ArgumentException("A roster cannot contain duplicate Steam IDs.", "players");
                }
            }

            Settings = settings;
            Players = (PlayerDescriptor[])players.Clone();
        }

        public MatchStartSettings Settings { get; private set; }

        public PlayerDescriptor[] Players { get; private set; }
    }

    public static class PacketCodec
    {
        public static byte[] EncodePeerHandshake(ModMessageType messageType, ulong manifestToken)
        {
            if (messageType != ModMessageType.Hello && messageType != ModMessageType.HelloAck)
            {
                throw new ArgumentException("Peer handshakes must use Hello or HelloAck.", "messageType");
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, messageType);
                writer.Write(ProtocolConstants.CompatibilityToken);
                writer.Write(manifestToken);
                return stream.ToArray();
            }
        }

        public static ulong DecodePeerHandshake(byte[] packet, ModMessageType messageType)
        {
            if (messageType != ModMessageType.Hello && messageType != ModMessageType.HelloAck)
            {
                throw new ArgumentException("Peer handshakes must use Hello or HelloAck.", "messageType");
            }

            using (var reader = CreateReader(packet))
            {
                ReadHeader(reader, messageType);
                if (reader.ReadUInt64() != ProtocolConstants.CompatibilityToken)
                {
                    throw new ProtocolException("Peer handshake uses a different BoplEight compatibility token.");
                }

                ulong manifestToken = reader.ReadUInt64();
                EnsureAtEnd(reader);
                return manifestToken;
            }
        }

        public static byte[] EncodeRosterControl(ModMessageType messageType, ushort sequenceNumber, ulong rosterToken)
        {
            ValidateRosterControlType(messageType);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, messageType);
                writer.Write(ProtocolConstants.CompatibilityToken);
                writer.Write(sequenceNumber);
                writer.Write(rosterToken);
                writer.Write((ushort)0);
                return stream.ToArray();
            }
        }

        public static ushort DecodeRosterControl(byte[] packet, ModMessageType messageType, out ulong rosterToken)
        {
            ValidateRosterControlType(messageType);
            using (var reader = CreateReader(packet))
            {
                ReadHeader(reader, messageType);
                if (reader.ReadUInt64() != ProtocolConstants.CompatibilityToken)
                {
                    throw new ProtocolException("Roster control uses a different BoplEight compatibility token.");
                }

                ushort sequenceNumber = reader.ReadUInt16();
                rosterToken = reader.ReadUInt64();
                if (reader.ReadUInt16() != 0)
                {
                    throw new ProtocolException("Roster control reserved bytes are invalid.");
                }

                EnsureAtEnd(reader);
                return sequenceNumber;
            }
        }

        public static ulong ComputeRosterToken(StartRoster roster)
        {
            if (roster == null)
            {
                throw new ArgumentNullException("roster");
            }

            byte[] encodedRoster = EncodeStartRoster(roster);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
            {
                digest = algorithm.ComputeHash(encodedRoster);
            }

            ulong token = 0;
            for (var index = 0; index < sizeof(ulong); index++)
            {
                token |= (ulong)digest[index] << (index * 8);
            }

            return token;
        }

        public static byte[] EncodeStartRoster(StartRoster roster)
        {
            if (roster == null)
            {
                throw new ArgumentNullException("roster");
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, ModMessageType.StartRoster);
                writer.Write(roster.Settings.SequenceNumber);
                writer.Write(roster.Settings.Seed);
                writer.Write(roster.Settings.AbilityCount);
                writer.Write(roster.Settings.Level);
                writer.Write(roster.Settings.FrameBufferSize);
                writer.Write(roster.Settings.DemoMask);
                writer.Write((byte)roster.Players.Length);

                for (var index = 0; index < roster.Players.Length; index++)
                {
                    PlayerDescriptor player = roster.Players[index];
                    writer.Write(player.Slot);
                    writer.Write(player.SteamId);
                    writer.Write(player.PlayerColorId);
                    writer.Write(player.TeamId);
                    writer.Write((byte)(player.UsesKeyboardAndMouse ? 1 : 0));
                    writer.Write(player.AbilityIds);
                    writer.Write((byte)0);
                }

                return stream.ToArray();
            }
        }

        public static StartRoster DecodeStartRoster(byte[] packet)
        {
            using (var reader = CreateReader(packet))
            {
                ReadHeader(reader, ModMessageType.StartRoster);
                var settings = new MatchStartSettings(
                    reader.ReadUInt16(),
                    reader.ReadUInt32(),
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte());
                var playerCount = reader.ReadByte();
                if (playerCount < ProtocolConstants.MinimumPlayers || playerCount > ProtocolConstants.MaximumPlayers)
                {
                    throw new ProtocolException("Start roster player count is invalid.");
                }

                var players = new PlayerDescriptor[playerCount];
                for (var index = 0; index < players.Length; index++)
                {
                    var slot = reader.ReadByte();
                    var steamId = reader.ReadUInt64();
                    var playerColorId = reader.ReadByte();
                    var teamId = reader.ReadByte();
                    var inputDevice = reader.ReadByte();
                    if (inputDevice > 1)
                    {
                        throw new ProtocolException("Start roster input-device metadata is invalid.");
                    }

                    var abilities = reader.ReadBytes(ProtocolConstants.AbilityCount);
                    if (abilities.Length != ProtocolConstants.AbilityCount)
                    {
                        throw new ProtocolException("Start roster ended before all ability selections were received.");
                    }

                    if (reader.ReadByte() != 0)
                    {
                        throw new ProtocolException("Start roster reserved bytes are invalid.");
                    }

                    players[index] = new PlayerDescriptor(
                        slot,
                        steamId,
                        playerColorId,
                        teamId,
                        inputDevice != 0,
                        abilities);
                }

                EnsureAtEnd(reader);
                try
                {
                    return new StartRoster(settings, players);
                }
                catch (ArgumentException exception)
                {
                    throw new ProtocolException(exception.Message);
                }
            }
        }

        private static void WriteHeader(BinaryWriter writer, ModMessageType messageType)
        {
            writer.Write((byte)messageType);
            writer.Write(ProtocolConstants.Magic);
            writer.Write(ProtocolConstants.Version);
        }

        private static void ValidateRosterControlType(ModMessageType messageType)
        {
            if (messageType != ModMessageType.RosterAck
                && messageType != ModMessageType.CommitRoster
                && messageType != ModMessageType.RejectRoster
                && messageType != ModMessageType.CommitAck)
            {
                throw new ArgumentException("Roster controls must use RosterAck, CommitRoster, RejectRoster, or CommitAck.", "messageType");
            }
        }

        private static BinaryReader CreateReader(byte[] packet)
        {
            if (packet == null)
            {
                throw new ArgumentNullException("packet");
            }

            return new BinaryReader(new MemoryStream(packet, false));
        }

        private static void ReadHeader(BinaryReader reader, ModMessageType expectedMessageType)
        {
            try
            {
                if (reader.ReadByte() != (byte)expectedMessageType)
                {
                    throw new ProtocolException("Packet type is invalid for this handler.");
                }

                if (reader.ReadUInt16() != ProtocolConstants.Magic)
                {
                    throw new ProtocolException("Packet is not a BoplEight protocol packet.");
                }

                if (reader.ReadByte() != ProtocolConstants.Version)
                {
                    throw new ProtocolException("Packet uses an unsupported protocol version.");
                }
            }
            catch (EndOfStreamException)
            {
                throw new ProtocolException("Packet ended before its protocol header was received.");
            }
        }

        private static void EnsureAtEnd(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new ProtocolException("Packet contains trailing bytes.");
            }
        }
    }

    public static class VanillaLobbyPacketValidator
    {
        public static bool IsValid(byte[] packet, int abilityCount)
        {
            if (packet == null)
            {
                return false;
            }

            if (packet.Length == 3)
            {
                return packet[0] < ProtocolConstants.PlayerColorCount
                    && packet[1] < ProtocolConstants.MaximumPlayers
                    && packet[2] <= 1;
            }

            if (packet.Length == 4)
            {
                return packet[0] < abilityCount
                    && packet[1] < abilityCount
                    && packet[2] < abilityCount;
            }

            if (packet.Length == 15)
            {
                return packet[0] < ProtocolConstants.PlayerColorCount
                    && packet[1] < ProtocolConstants.MaximumPlayers
                    && packet[2] < abilityCount
                    && packet[3] < abilityCount
                    && packet[4] < abilityCount
                    && packet[5] <= 1
                    && packet[6] <= 1;
            }

            if (packet.Length == 2)
            {
                return packet[0] >= 1 && packet[0] <= ProtocolConstants.AbilityCount;
            }

            return true;
        }
    }
}
