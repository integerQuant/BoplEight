using System;

namespace BoplEight.Ui
{
    internal static class RosterLayout
    {
        internal const float Scale = 0.4f;

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
                : vanillaSeparation * 3f / (playerCount - 1);
        }

        internal static float FittedScale(float originalScale)
        {
            return originalScale * Scale;
        }

        internal static float FittedRootPosition(float rootPosition, float visualBaseline)
        {
            return visualBaseline + (rootPosition - visualBaseline) * Scale;
        }

        internal static bool IsExpandedRemoteSlot(string name)
        {
            return name != null && name.StartsWith("BoplEight Remote Slot ", StringComparison.Ordinal);
        }
    }
}
