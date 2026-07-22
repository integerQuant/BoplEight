using System;
using System.IO;

namespace BoplEight.Protocol
{
    public enum PacketRouteKind
    {
        NotBoplEightPacket,
        Hello,
        HelloAck,
        RosterAck,
        CommitRoster,
        CommitAck,
        RejectRoster,
        StartRoster,
        RejectedBoplEightPacket
    }

    public sealed class PacketRoute
    {
        private PacketRoute(PacketRouteKind kind, StartRoster startRoster, ushort sequenceNumber, ulong rosterToken, ulong manifestToken, string reason)
        {
            Kind = kind;
            StartRoster = startRoster;
            SequenceNumber = sequenceNumber;
            RosterToken = rosterToken;
            ManifestToken = manifestToken;
            Reason = reason;
        }

        public PacketRouteKind Kind { get; private set; }

        public StartRoster StartRoster { get; private set; }

        public ushort SequenceNumber { get; private set; }

        public ulong RosterToken { get; private set; }

        public ulong ManifestToken { get; private set; }

        public string Reason { get; private set; }

        public static PacketRoute NotBoplEightPacket()
        {
            return new PacketRoute(PacketRouteKind.NotBoplEightPacket, null, 0, 0, 0, null);
        }

        public static PacketRoute StartRosterPacket(StartRoster startRoster)
        {
            return new PacketRoute(PacketRouteKind.StartRoster, startRoster, startRoster.Settings.SequenceNumber, PacketCodec.ComputeRosterToken(startRoster), 0, null);
        }

        public static PacketRoute HelloPacket(ulong manifestToken)
        {
            return new PacketRoute(PacketRouteKind.Hello, null, 0, 0, manifestToken, null);
        }

        public static PacketRoute HelloAckPacket(ulong manifestToken)
        {
            return new PacketRoute(PacketRouteKind.HelloAck, null, 0, 0, manifestToken, null);
        }

        public static PacketRoute RosterControlPacket(PacketRouteKind kind, ushort sequenceNumber, ulong rosterToken)
        {
            return new PacketRoute(kind, null, sequenceNumber, rosterToken, 0, null);
        }

        public static PacketRoute Rejected(string reason)
        {
            return new PacketRoute(PacketRouteKind.RejectedBoplEightPacket, null, 0, 0, 0, reason);
        }
    }

    public static class PacketRouter
    {
        public static PacketRoute Route(byte[] packet)
        {
            if (packet == null || packet.Length == 0)
            {
                return PacketRoute.NotBoplEightPacket();
            }

            if (!IsReservedBoplEightMessageType(packet[0]))
            {
                return PacketRoute.NotBoplEightPacket();
            }

            if (packet.Length < 4)
            {
                return PacketRoute.Rejected("The BoplEight packet ended before its protocol header was received.");
            }

            if (packet[1] != (byte)(ProtocolConstants.Magic & 0xff)
                || packet[2] != (byte)(ProtocolConstants.Magic >> 8))
            {
                return PacketRoute.NotBoplEightPacket();
            }

            try
            {
                switch ((ModMessageType)packet[0])
                {
                    case ModMessageType.Hello:
                        return PacketRoute.HelloPacket(PacketCodec.DecodePeerHandshake(packet, ModMessageType.Hello));
                    case ModMessageType.HelloAck:
                        return PacketRoute.HelloAckPacket(PacketCodec.DecodePeerHandshake(packet, ModMessageType.HelloAck));
                    case ModMessageType.StartRoster:
                        return PacketRoute.StartRosterPacket(PacketCodec.DecodeStartRoster(packet));
                    case ModMessageType.RosterAck:
                    {
                        ulong rosterToken;
                        return PacketRoute.RosterControlPacket(
                            PacketRouteKind.RosterAck,
                            PacketCodec.DecodeRosterControl(packet, ModMessageType.RosterAck, out rosterToken),
                            rosterToken);
                    }
                    case ModMessageType.CommitRoster:
                    {
                        ulong rosterToken;
                        return PacketRoute.RosterControlPacket(
                            PacketRouteKind.CommitRoster,
                            PacketCodec.DecodeRosterControl(packet, ModMessageType.CommitRoster, out rosterToken),
                            rosterToken);
                    }
                    case ModMessageType.RejectRoster:
                    {
                        ulong rosterToken;
                        return PacketRoute.RosterControlPacket(
                            PacketRouteKind.RejectRoster,
                            PacketCodec.DecodeRosterControl(packet, ModMessageType.RejectRoster, out rosterToken),
                            rosterToken);
                    }
                    case ModMessageType.CommitAck:
                    {
                        ulong rosterToken;
                        return PacketRoute.RosterControlPacket(
                            PacketRouteKind.CommitAck,
                            PacketCodec.DecodeRosterControl(packet, ModMessageType.CommitAck, out rosterToken),
                            rosterToken);
                    }
                    default:
                        return PacketRoute.Rejected("The BoplEight packet type is unsupported.");
                }
            }
            catch (ProtocolException exception)
            {
                return PacketRoute.Rejected(exception.Message);
            }
            catch (EndOfStreamException)
            {
                return PacketRoute.Rejected("The BoplEight packet ended before its payload was complete.");
            }
            catch (ArgumentException exception)
            {
                return PacketRoute.Rejected(exception.Message);
            }
        }

        public static bool IsReservedBoplEightMessageType(byte messageType)
        {
            return messageType >= (byte)ModMessageType.Hello
                && messageType <= (byte)ModMessageType.CommitAck;
        }
    }
}
