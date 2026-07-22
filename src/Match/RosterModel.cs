using System;
using BoplEight.Protocol;

namespace BoplEight.Match
{
    public sealed class RosterState
    {
        public StartRoster ActiveRoster { get; private set; }

        public bool TryActivate(StartRoster roster, ulong senderSteamId, out string reason)
        {
            if (roster == null)
            {
                reason = "The BoplEight start roster was empty.";
                return false;
            }

            var senderIsInRoster = false;
            for (var index = 0; index < roster.Players.Length; index++)
            {
                if (roster.Players[index].SteamId == senderSteamId)
                {
                    senderIsInRoster = true;
                    break;
                }
            }

            if (!senderIsInRoster)
            {
                reason = "The BoplEight start roster did not include its sender.";
                return false;
            }

            if (ActiveRoster != null
                && !IsNewerSequence(roster.Settings.SequenceNumber, ActiveRoster.Settings.SequenceNumber))
            {
                reason = "The BoplEight start roster sequence was stale or duplicated.";
                return false;
            }

            ActiveRoster = roster;
            reason = null;
            return true;
        }

        public void Clear()
        {
            ActiveRoster = null;
        }

        public static bool IsNewerSequence(ushort candidate, ushort current)
        {
            return candidate != current && (ushort)(candidate - current) < 0x8000;
        }
    }

    public sealed class RosterFrame<T>
    {
        private readonly bool[] rosterSlots = new bool[ProtocolConstants.MaximumPlayers];
        private readonly bool[] populatedSlots = new bool[ProtocolConstants.MaximumPlayers];
        private readonly T[] values = new T[ProtocolConstants.MaximumPlayers];
        private readonly int rosterSlotCount;
        private int populatedSlotCount;

        public RosterFrame(byte[] slots)
        {
            if (slots == null)
            {
                throw new ArgumentNullException("slots");
            }

            if (slots.Length < ProtocolConstants.MinimumPlayers || slots.Length > ProtocolConstants.MaximumPlayers)
            {
                throw new ArgumentException("A frame roster must contain between two and eight slots.", "slots");
            }

            for (var index = 0; index < slots.Length; index++)
            {
                byte slot = slots[index];
                if (slot >= ProtocolConstants.MaximumPlayers)
                {
                    throw new ArgumentException("Frame slots must be between 0 and 7.", "slots");
                }

                if (rosterSlots[slot])
                {
                    throw new ArgumentException("A frame roster cannot contain duplicate slots.", "slots");
                }

                rosterSlots[slot] = true;
            }

            rosterSlotCount = slots.Length;
        }

        public bool IsComplete
        {
            get { return populatedSlotCount == rosterSlotCount; }
        }

        public void Set(byte slot, T value)
        {
            if (slot >= ProtocolConstants.MaximumPlayers || !rosterSlots[slot])
            {
                throw new ArgumentException("The input slot is not part of this frame's roster.", "slot");
            }

            if (populatedSlots[slot])
            {
                throw new ArgumentException("A frame cannot contain duplicate input slots.", "slot");
            }

            values[slot] = value;
            populatedSlots[slot] = true;
            populatedSlotCount++;
        }

        public T Get(byte slot)
        {
            if (slot >= ProtocolConstants.MaximumPlayers || !populatedSlots[slot])
            {
                throw new InvalidOperationException("The requested frame slot has no input.");
            }

            return values[slot];
        }
    }
}
