using System;

namespace BoplEight.Ui
{
    internal static class RosterLayout
    {
        internal const float Scale = 0.4f;
        internal const float ScorePortraitSeparation = 108f;

        internal static float ColumnCenter(float firstCenter, float lastCenter, int index, int count)
        {
            if (count <= 1)
            {
                return (firstCenter + lastCenter) * 0.5f;
            }

            return firstCenter + (lastCenter - firstCenter) * index / (count - 1);
        }

        internal static float FittedSeparation(float vanillaSeparation, int playerCount)
        {
            return playerCount <= 4
                ? vanillaSeparation
                : ScorePortraitSeparation;
        }

        internal static float FittedScale(float originalScale)
        {
            return originalScale * Scale;
        }

        internal static float FittedRootPosition(float rootPosition, float visualBaseline)
        {
            return visualBaseline + (rootPosition - visualBaseline) * Scale;
        }

        internal static float FittedAnimationBoundary(float boundary, float restingPosition)
        {
            return restingPosition + (boundary - restingPosition) / Scale;
        }

        internal static float InitialAnimationPosition(float startBoundary, float restingPosition)
        {
            return FittedAnimationBoundary(startBoundary, restingPosition);
        }

        internal static float FittedRoundSummaryDelaySpacing(float vanillaSpacing, int playerCount)
        {
            return playerCount <= 4
                ? vanillaSpacing
                : vanillaSpacing * 5f / (playerCount + 1);
        }

        internal static bool IsExpandedRemoteSlot(string name)
        {
            return name != null && name.StartsWith("BoplEight Remote Slot ", StringComparison.Ordinal);
        }
    }
}
