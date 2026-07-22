using System;

namespace BoplEight.Ui
{
    internal static class RosterLayout
    {
        internal const float Scale = 0.72f;
        internal const float Gutter = 12f;

        internal static float RowSpacing(float unscaledHeight, float minimumHeight)
        {
            return Math.Max(minimumHeight, unscaledHeight * Scale) + Gutter;
        }

        internal static float RowCenter(float rowSpacing, int row)
        {
            return (row == 0 ? 0.5f : -0.5f) * rowSpacing;
        }

        internal static bool IsExpandedRemoteSlot(string name)
        {
            return name != null && name.StartsWith("BoplEight Remote Slot ", StringComparison.Ordinal);
        }
    }
}
