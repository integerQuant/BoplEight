using System;
using System.Collections.Generic;
using System.Reflection;
using BoplEight.Colors;
using HarmonyLib;
using UnityEngine;

namespace BoplEight.Runtime
{
    internal static class PaletteRuntime
    {
        private const int VanillaColorCount = 4;
        private const int ExtendedColorCount = 8;

        internal static void ExtendPlayerColors(PlayerColors playerColors)
        {
            if (playerColors == null)
            {
                return;
            }

            FieldInfo colorsField = AccessTools.Field(typeof(PlayerColors), "playerColors");
            if (colorsField == null)
            {
                BoplEightPlugin.Log.LogWarning("Could not find the PlayerColors array for BoplEight's extended palette.");
                return;
            }

            var colors = (PlayerColor[])colorsField.GetValue(playerColors);
            if (colors == null || colors.Length >= ExtendedColorCount || colors.Length < VanillaColorCount)
            {
                return;
            }

            var extended = new PlayerColor[ExtendedColorCount];
            Array.Copy(colors, extended, colors.Length);
            FieldInfo colorIndexField = AccessTools.Field(typeof(PlayerColor), "colorIndex");
            FieldInfo uiMaterialField = AccessTools.Field(typeof(PlayerColor), "uiMaterial");
            FieldInfo playerMaterialField = AccessTools.Field(typeof(PlayerColor), "playerMaterial");
            if (colorIndexField == null || uiMaterialField == null || playerMaterialField == null)
            {
                BoplEightPlugin.Log.LogWarning("Could not find PlayerColor fields for BoplEight's extended palette.");
                return;
            }

            for (var index = VanillaColorCount; index < ExtendedColorCount; index++)
            {
                object color = extended[index - VanillaColorCount];
                PaletteColor paletteColor = TeamPalette.ExtendedTeamColors[index - VanillaColorCount];
                colorIndexField.SetValue(color, index);
                uiMaterialField.SetValue(color, CloneMaterial((Material)uiMaterialField.GetValue(color), paletteColor, "BoplEight UI Color " + index));
                playerMaterialField.SetValue(color, CloneMaterial((Material)playerMaterialField.GetValue(color), paletteColor, "BoplEight Player Color " + index));
                extended[index] = (PlayerColor)color;
            }

            colorsField.SetValue(playerColors, extended);
            BoplEightPlugin.Log.LogInfo("Extended player colors from " + colors.Length + " to " + extended.Length + ".");
        }

        internal static void ExtendTeamColors(TeamColors teamColors)
        {
            if (teamColors == null)
            {
                return;
            }

            FieldInfo colorsField = AccessTools.Field(typeof(TeamColors), "teamColors");
            if (colorsField == null)
            {
                BoplEightPlugin.Log.LogWarning("Could not find the TeamColors array for BoplEight's extended palette.");
                return;
            }

            var colors = (TeamColor[])colorsField.GetValue(teamColors);
            if (colors == null || colors.Length >= ExtendedColorCount || colors.Length < VanillaColorCount)
            {
                return;
            }

            FieldInfo teamField = AccessTools.Field(typeof(TeamColor), "team");
            FieldInfo fillField = AccessTools.Field(typeof(TeamColor), "fill");
            FieldInfo borderField = AccessTools.Field(typeof(TeamColor), "border");
            FieldInfo saturatedField = AccessTools.Field(typeof(TeamColor), "saturated");
            if (teamField == null || fillField == null || borderField == null || saturatedField == null)
            {
                BoplEightPlugin.Log.LogWarning("Could not find TeamColor fields for BoplEight's extended palette.");
                return;
            }

            var extended = new TeamColor[ExtendedColorCount];
            Array.Copy(colors, extended, colors.Length);
            for (var index = VanillaColorCount; index < ExtendedColorCount; index++)
            {
                PaletteColor paletteColor = TeamPalette.ExtendedTeamColors[index - VanillaColorCount];
                Color fill = ToUnityColor(paletteColor);
                object teamColor = new TeamColor();
                teamField.SetValue(teamColor, index);
                fillField.SetValue(teamColor, fill);
                borderField.SetValue(teamColor, new Color(fill.r * 0.55f, fill.g * 0.55f, fill.b * 0.55f, 1f));
                saturatedField.SetValue(teamColor, fill);
                extended[index] = (TeamColor)teamColor;
            }

            colorsField.SetValue(teamColors, extended);
            BoplEightPlugin.Log.LogInfo("Extended team colors from " + colors.Length + " to " + extended.Length + ".");
        }

        internal static void ExtendColorPool(ColorPool colorPool)
        {
            if (colorPool == null)
            {
                return;
            }

            FieldInfo colorsField = AccessTools.Field(typeof(ColorPool), "colors");
            if (colorsField == null)
            {
                return;
            }

            var colors = (List<Material>)colorsField.GetValue(colorPool);
            if (colors == null || colors.Count >= ExtendedColorCount || colors.Count < VanillaColorCount)
            {
                return;
            }

            for (var index = VanillaColorCount; index < ExtendedColorCount; index++)
            {
                colors.Add(CloneMaterial(colors[index - VanillaColorCount], TeamPalette.ExtendedTeamColors[index - VanillaColorCount], "BoplEight Pool Color " + index));
            }
        }

        private static Material CloneMaterial(Material source, PaletteColor color, string name)
        {
            if (source == null)
            {
                return null;
            }

            Material clone = UnityEngine.Object.Instantiate(source);
            clone.name = name;
            if (clone.HasProperty("_Color"))
            {
                clone.color = ToUnityColor(color);
            }

            Color fill = ToUnityColor(color);
            SetColorIfPresent(clone, "_Blue", fill);
            SetColorIfPresent(clone, "_ShadowColor", new Color(fill.r * 0.55f, fill.g * 0.55f, fill.b * 0.55f, 1f));
            SetColorIfPresent(clone, "_HighlightColor", Color.Lerp(fill, Color.white, 0.35f));

            return clone;
        }

        private static void SetColorIfPresent(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        private static Color ToUnityColor(PaletteColor color)
        {
            return new Color(color.Red / 255f, color.Green / 255f, color.Blue / 255f, 1f);
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "Awake")]
    internal static class CharacterSelectPalettePatch
    {
        private static void Postfix(CharacterSelectHandler_online __instance)
        {
            FieldInfo playerColorsField = AccessTools.Field(typeof(CharacterSelectHandler_online), "playerColors");
            if (playerColorsField != null)
            {
                PaletteRuntime.ExtendPlayerColors((PlayerColors)playerColorsField.GetValue(__instance));
            }
        }
    }

    [HarmonyPatch(typeof(CSBox_online), "Awake")]
    internal static class CSBoxPalettePatch
    {
        private static void Postfix(CSBox_online __instance)
        {
            FieldInfo playerColorsField = AccessTools.Field(typeof(CSBox_online), "playerColors");
            FieldInfo teamColorsField = AccessTools.Field(typeof(CSBox_online), "teamColors");
            if (playerColorsField != null)
            {
                PaletteRuntime.ExtendPlayerColors((PlayerColors)playerColorsField.GetValue(__instance));
            }

            if (teamColorsField != null)
            {
                PaletteRuntime.ExtendTeamColors((TeamColors)teamColorsField.GetValue(__instance));
            }
        }
    }

    [HarmonyPatch(typeof(ColorPool), "Start")]
    internal static class ColorPoolPalettePatch
    {
        private static void Prefix(ColorPool __instance)
        {
            PaletteRuntime.ExtendColorPool(__instance);
        }
    }
}
