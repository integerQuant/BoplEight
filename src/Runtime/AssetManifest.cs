namespace BoplEight.Runtime
{
    internal static class AssetManifest
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        internal static ulong CurrentToken()
        {
            if (SteamManager.instance == null
                || SteamManager.instance.abilityIcons == null
                || SteamManager.instance.abilityIcons.sprites == null)
            {
                return 0;
            }

            ulong hash = OffsetBasis;
            Mix(ref hash, SteamManager.instance.dlc.HasDLC() ? (byte)1 : (byte)0);
            NamedSpriteList abilities = SteamManager.instance.abilityIcons;
            MixInt(ref hash, abilities.sprites.Count);
            for (var index = 0; index < abilities.sprites.Count; index++)
            {
                NamedSprite ability = abilities.sprites[index];
                MixString(ref hash, ability.name);
                MixString(ref hash, ability.associatedGameObject == null ? null : ability.associatedGameObject.name);
                Mix(ref hash, ability.isOffensiveAbility ? (byte)1 : (byte)0);
            }

            return hash;
        }

        private static void MixString(ref ulong hash, string value)
        {
            if (value == null)
            {
                MixInt(ref hash, -1);
                return;
            }

            MixInt(ref hash, value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                char character = value[index];
                Mix(ref hash, (byte)(character & 0xff));
                Mix(ref hash, (byte)(character >> 8));
            }
        }

        private static void MixInt(ref ulong hash, int value)
        {
            Mix(ref hash, (byte)value);
            Mix(ref hash, (byte)(value >> 8));
            Mix(ref hash, (byte)(value >> 16));
            Mix(ref hash, (byte)(value >> 24));
        }

        private static void Mix(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= Prime;
        }
    }
}
