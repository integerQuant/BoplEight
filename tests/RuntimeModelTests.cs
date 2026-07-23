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

        public static void RosterLayoutPlacesEightPlayersInUniqueColumns()
        {
            const float left = -420f;
            const float right = 420f;
            float previous = RosterLayout.ColumnCenter(left, right, 0, ProtocolConstants.MaximumPlayers);

            for (var slot = 1; slot < ProtocolConstants.MaximumPlayers; slot++)
            {
                float current = RosterLayout.ColumnCenter(left, right, slot, ProtocolConstants.MaximumPlayers);
                Assert.True(current > previous, "Every player must occupy a distinct left-to-right column.");
                previous = current;
            }
        }

        public static void RosterLayoutKeepsPlayerFiveSeparateFromPlayerOne()
        {
            float playerOne = RosterLayout.ColumnCenter(-420f, 420f, 0, ProtocolConstants.MaximumPlayers);
            float playerFive = RosterLayout.ColumnCenter(-420f, 420f, 4, ProtocolConstants.MaximumPlayers);

            Assert.False(playerOne == playerFive, "Player five must not wrap onto player one's column.");
        }

        public static void RosterLayoutFitsEightAbilitySelectorsAcrossVanillaWidth()
        {
            const float vanillaSeparation = 200f;
            float fitted = RosterLayout.FittedSeparation(vanillaSeparation, ProtocolConstants.MaximumPlayers);

            Assert.Equal(RosterLayout.ScorePortraitSeparation, fitted, "Eight score portraits must use their measured non-overlapping card separation.");
        }

        public static void RosterLayoutPreservesPrefabScaleWhenFittingEightColumns()
        {
            const float originalButtonScale = 0.2843f;
            const float unscaledButtonWidth = 64f;
            const float originalFirstCenter = 3.5f;
            const float originalLastCenter = 63.5f;
            float fittedScale = RosterLayout.FittedScale(originalButtonScale);
            float eightColumnSpacing = (originalLastCenter - originalFirstCenter) / 7f;

            Assert.True(fittedScale < originalButtonScale, "Eight-column fitting must multiply the prefab scale instead of replacing it.");
            Assert.True(unscaledButtonWidth * fittedScale < eightColumnSpacing, "Fitted Steam portrait hitboxes must not overlap adjacent columns.");
        }

        public static void RosterLayoutKeepsOffsetReadyCardOnVisualBaseline()
        {
            const float localRoot = -51f;
            const float remoteVisualBaseline = -610f;
            float fittedRoot = RosterLayout.FittedRootPosition(localRoot, remoteVisualBaseline);
            float fittedChildOffset = (remoteVisualBaseline - localRoot) * RosterLayout.Scale;

            Assert.Equal(remoteVisualBaseline, fittedRoot + fittedChildOffset, "Scaling the local selector root must keep its offset ready card aligned with remote cards.");
        }

        public static void RosterLayoutExpandsAnimationTravelForScaledCards()
        {
            float fitted = RosterLayout.FittedAnimationBoundary(3000f, 0f);

            Assert.True(System.Math.Abs(fitted - 7500f) < 0.01f, "Scaled character cards must retain the original screen-space animation travel.");
        }

        public static void RosterLayoutStartsScaledControlsAtTheFittedBoundary()
        {
            float initialPosition = RosterLayout.InitialAnimationPosition(3000f, 0f);

            Assert.True(System.Math.Abs(initialPosition - 7500f) < 0.01f, "Controls hidden on first lobby load must use the fitted off-screen boundary before their first animation.");
        }

        public static void RosterLayoutPreservesEnteringJoinControl()
        {
            Assert.False(
                RosterLayout.ShouldSetInitialAnimationPosition(true),
                "The auto-entering join control must not be reset off-screen.");
            Assert.True(
                RosterLayout.ShouldSetInitialAnimationPosition(false),
                "Inactive template controls must begin at the fitted off-screen boundary.");
        }

        public static void RosterLayoutKeepsScorePortraitsCompact()
        {
            const float vanillaSeparation = 230f;
            const float visualCardWidth = 107.17f;
            float fivePlayerSeparation = RosterLayout.FittedSeparation(vanillaSeparation, 5);
            float eightPlayerSeparation = RosterLayout.FittedSeparation(vanillaSeparation, 8);

            Assert.True(fivePlayerSeparation >= visualCardWidth, "Five score portraits must not overlap.");
            Assert.True(eightPlayerSeparation >= visualCardWidth, "Eight score portraits must not overlap.");
            Assert.Equal(RosterLayout.ScorePortraitSeparation, fivePlayerSeparation, "Five score portraits must use the compact measured separation.");
            Assert.Equal(RosterLayout.ScorePortraitSeparation, eightPlayerSeparation, "Eight score portraits must use the compact measured separation.");
        }

        public static void RosterLayoutPreservesRoundSummaryTiming()
        {
            const float vanillaSpacing = 0.3f;
            const float vanillaNextLevelStagger = vanillaSpacing * 5f;
            float fivePlayerStagger = RosterLayout.FittedRoundSummaryDelaySpacing(vanillaSpacing, 5) * 6f;
            float eightPlayerStagger = RosterLayout.FittedRoundSummaryDelaySpacing(vanillaSpacing, 8) * 9f;

            Assert.True(System.Math.Abs(fivePlayerStagger - vanillaNextLevelStagger) < 0.01f, "Five players must not delay the next-round control reveal.");
            Assert.True(System.Math.Abs(eightPlayerStagger - vanillaNextLevelStagger) < 0.01f, "Eight players must retain the four-player next-round control timing.");
        }

        public static void SparseAvatarReadinessPreservesConnectionSlots()
        {
            var ready = new bool[] { true, false, false, true };

            Assert.Equal(0, AvatarSlotMapping.ConnectionIndexForSquare(0, ready.Length, ready[0]), "The first ready avatar should remain in the first connection slot.");
            Assert.Equal(-1, AvatarSlotMapping.ConnectionIndexForSquare(1, ready.Length, ready[1]), "An unfinished avatar must leave its own slot loading.");
            Assert.Equal(3, AvatarSlotMapping.ConnectionIndexForSquare(3, ready.Length, ready[3]), "A later completed avatar must not disappear behind an unfinished earlier request.");
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
