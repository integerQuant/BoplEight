namespace BoplEight.Colors
{
    public struct PaletteColor
    {
        public PaletteColor(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public readonly byte Red;

        public readonly byte Green;

        public readonly byte Blue;

        public string ToStableKey()
        {
            return Red + ":" + Green + ":" + Blue;
        }
    }

    public static class TeamPalette
    {
        // The first four entries remain the game's vanilla palette. These are the four new player-selectable teams.
        public static readonly PaletteColor[] ExtendedTeamColors =
        {
            new PaletteColor(157, 77, 255),
            new PaletteColor(31, 191, 189),
            new PaletteColor(244, 86, 151),
            new PaletteColor(245, 181, 57)
        };
    }
}
