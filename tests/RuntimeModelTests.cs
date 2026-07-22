using BoplEight.Match;
using BoplEight.Protocol;
using BoplEight.Ui;

namespace BoplEight.Tests
{
    internal static class RuntimeModelTests
    {
        public static void RosterStateAcceptsStrictlyNewerHostRosters()
        {
            var state = new RosterState();
            string reason;

            Assert.True(state.TryActivate(NewRoster(10), 1001, out reason), "The first owner roster should activate. Reason: " + reason);
            Assert.True(state.TryActivate(NewRoster(11), 1001, out reason), "A newer owner roster should replace the active roster. Reason: " + reason);
            Assert.Equal((ushort)11, state.ActiveRoster.Settings.SequenceNumber, "The newest roster should remain active.");
        }

        public static void RosterStateAcceptsSequenceWraparound()
        {
            var state = new RosterState();
            string reason;

            Assert.True(state.TryActivate(NewRoster(ushort.MaxValue), 1001, out reason), "The pre-wrap roster should activate. Reason: " + reason);
            Assert.True(state.TryActivate(NewRoster(0), 1001, out reason), "Sequence zero should be newer after ushort wraparound. Reason: " + reason);
        }

        public static void RosterStateRejectsStaleAndForeignRosters()
        {
            var state = new RosterState();
            string reason;

            Assert.False(state.TryActivate(NewRoster(20), 9999, out reason), "A sender absent from the roster must be rejected.");
            Assert.True(state.TryActivate(NewRoster(20), 1001, out reason), "A valid initial roster should activate. Reason: " + reason);
            Assert.False(state.TryActivate(NewRoster(20), 1001, out reason), "A duplicate sequence must be rejected.");
            Assert.False(state.TryActivate(NewRoster(19), 1001, out reason), "An older sequence must be rejected.");
        }

        public static void RosterFramesSupportSparseEightPlayerSlots()
        {
            var frame = new RosterFrame<int>(new byte[] { 0, 3, 7 });

            frame.Set(0, 10);
            frame.Set(3, 30);
            Assert.False(frame.IsComplete, "A sparse frame should remain incomplete until every roster slot has input.");
            frame.Set(7, 70);

            Assert.True(frame.IsComplete, "Slots zero through seven must be representable without requiring contiguous players.");
            Assert.Equal(70, frame.Get(7), "The eighth simulation slot should retain its input.");
            Assert.Throws<System.ArgumentException>(delegate { frame.Set(6, 60); }, "Frames must reject inputs from slots outside their roster.");
        }

        public static void RosterLayoutSeparatesScaledRows()
        {
            const float panelHeight = 200f;
            float spacing = RosterLayout.RowSpacing(panelHeight, 120f);
            float scaledHeight = panelHeight * RosterLayout.Scale;

            Assert.True(spacing > scaledHeight, "Two-row selectors need a gutter between their scaled bounds.");
            Assert.Equal(spacing, RosterLayout.RowCenter(spacing, 0) - RosterLayout.RowCenter(spacing, 1), "Row centers should be exactly one row spacing apart.");
        }

        public static void RosterLayoutUsesLobbyMinimumWithGutter()
        {
            float spacing = RosterLayout.RowSpacing(48f, 120f);

            Assert.Equal(132f, spacing, "Small portrait overlays must use the selection panel minimum spacing and gutter.");
        }

        public static void RosterLayoutIdentifiesOnlyExpandedRemoteSlots()
        {
            Assert.True(RosterLayout.IsExpandedRemoteSlot("BoplEight Remote Slot 5"), "Expanded remote slots must be activated before initialization.");
            Assert.False(RosterLayout.IsExpandedRemoteSlot("Remote Slot 5"), "Vanilla slots must retain their existing lifecycle.");
            Assert.False(RosterLayout.IsExpandedRemoteSlot(null), "Missing slot names must not be treated as expanded slots.");
        }

        private static StartRoster NewRoster(ushort sequence)
        {
            return new StartRoster(
                new MatchStartSettings(sequence, 123, 3, 4, 8, 3),
                new PlayerDescriptor[]
                {
                    ProtocolTests.NewPlayer(0, 1001, 0, 0, false, 1, 2, 3),
                    ProtocolTests.NewPlayer(7, 1008, 7, 7, true, 4, 5, 6)
                });
        }
    }
}
