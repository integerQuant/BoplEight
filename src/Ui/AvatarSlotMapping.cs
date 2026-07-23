namespace BoplEight.Ui
{
    internal static class AvatarSlotMapping
    {
        internal static int ConnectionIndexForSquare(int squareIndex, int connectionCount, bool hasAvatar)
        {
            return squareIndex >= 0 && squareIndex < connectionCount && hasAvatar
                ? squareIndex
                : -1;
        }
    }
}
