using System.Collections.Generic;
using BoplEight.Colors;

namespace BoplEight.Tests
{
    internal static class PaletteTests
    {
        public static void ExtendedTeamPaletteProvidesFourDistinctTeamChoices()
        {
            PaletteColor[] colors = TeamPalette.ExtendedTeamColors;
            var distinctColors = new HashSet<string>();

            for (var index = 0; index < colors.Length; index++)
            {
                distinctColors.Add(colors[index].ToStableKey());
            }

            Assert.Equal(4, colors.Length, "The mod should add exactly four team colors beyond vanilla's four.");
            Assert.Equal(4, distinctColors.Count, "Every added team color must remain visually distinct.");
        }
    }
}
