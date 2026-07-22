using System;
using System.Collections.Generic;
using BoplEight.Ui;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoplEight.Runtime
{
    internal static class LobbyUiRuntime
    {
        private const int RemotePlayerCapacity = 7;
        private const float MinimumLobbyRowSpacing = 120f;

        private static float GetLobbyRowSpacing(RectTransform fallbackRect)
        {
            CharacterSelectHandler_online handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
            RectTransform selectionRect = handler == null || handler.characterSelectBox == null
                ? fallbackRect
                : handler.characterSelectBox.GetComponent<RectTransform>();
            return RosterLayout.RowSpacing(selectionRect.rect.height, MinimumLobbyRowSpacing);
        }

        internal static void ExtendCharacterSelection(CharacterSelectHandler_online handler)
        {
            if (handler == null)
            {
                return;
            }

            PaletteRuntime.ExtendPlayerColors(handler.playerColors);
            TeamSelector[] teamSelectors = UnityEngine.Object.FindObjectsOfType<TeamSelector>();
            for (var index = 0; index < teamSelectors.Length; index++)
            {
                PaletteRuntime.ExtendTeamColors(teamSelectors[index].teams);
            }

            CSBox_online[] originalBoxes = handler.networkPlayerBoxes;
            Image[] originalCircles = handler.loadingCircles;
            if (originalBoxes == null || originalBoxes.Length >= RemotePlayerCapacity)
            {
                return;
            }

            if (originalBoxes.Length != 3 || originalCircles == null || originalCircles.Length != 3)
            {
                BoplEightPlugin.Log.LogWarning("BoplEight expected three vanilla remote character slots and left the selection layout unchanged.");
                return;
            }

            RectTransform localRect = handler.characterSelectBox.GetComponent<RectTransform>();
            var basePositions = new Vector2[4];
            basePositions[0] = localRect.anchoredPosition;
            for (var index = 0; index < originalBoxes.Length; index++)
            {
                basePositions[index + 1] = originalBoxes[index].GetComponent<RectTransform>().anchoredPosition;
            }

            float rowSpacing = RosterLayout.RowSpacing(localRect.rect.height, MinimumLobbyRowSpacing);
            Vector2 firstRowOffset = Vector2.up * (rowSpacing * 0.5f);
            Vector2 secondRowOffset = Vector2.down * (rowSpacing * 0.5f);
            localRect.anchoredPosition = basePositions[0] + firstRowOffset;
            localRect.localScale *= RosterLayout.Scale;

            var boxes = new List<CSBox_online>(RemotePlayerCapacity);
            for (var index = 0; index < originalBoxes.Length; index++)
            {
                RectTransform boxRect = originalBoxes[index].GetComponent<RectTransform>();
                boxRect.anchoredPosition = basePositions[index + 1] + firstRowOffset;
                boxRect.localScale *= RosterLayout.Scale;
                boxes.Add(originalBoxes[index]);
            }

            for (var column = 0; column < 4; column++)
            {
                CSBox_online source = originalBoxes[Math.Min(column, originalBoxes.Length - 1)];
                bool sourceWasActive = source.gameObject.activeSelf;
                CSBox_online clone;
                try
                {
                    source.gameObject.SetActive(false);
                    clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                }
                finally
                {
                    source.gameObject.SetActive(sourceWasActive);
                }

                clone.name = "BoplEight Remote Slot " + (column + 5);
                clone.connectedPlayer = null;
                clone.isVisible = false;
                RectTransform cloneRect = clone.GetComponent<RectTransform>();
                cloneRect.anchoredPosition = basePositions[column] + secondRowOffset;
                cloneRect.localScale = source.GetComponent<RectTransform>().localScale;
                boxes.Add(clone);
            }

            var circles = new List<Image>(RemotePlayerCapacity);
            var circleOffsets = new Vector2[3];
            for (var index = 0; index < originalCircles.Length; index++)
            {
                RectTransform circleRect = originalCircles[index].rectTransform;
                circleOffsets[index] = circleRect.anchoredPosition - basePositions[index + 1];
                circleRect.anchoredPosition = basePositions[index + 1] + firstRowOffset + circleOffsets[index];
                circleRect.localScale *= RosterLayout.Scale;
                circles.Add(originalCircles[index]);
            }

            for (var column = 0; column < 4; column++)
            {
                int sourceIndex = Math.Min(column, originalCircles.Length - 1);
                Image source = originalCircles[sourceIndex];
                Image clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                clone.name = "BoplEight Loading Slot " + (column + 5);
                clone.rectTransform.anchoredPosition = basePositions[column] + secondRowOffset + circleOffsets[sourceIndex];
                clone.rectTransform.localScale = source.rectTransform.localScale;
                clone.enabled = false;
                circles.Add(clone);
            }

            handler.networkPlayerBoxes = boxes.ToArray();
            handler.loadingCircles = circles.ToArray();
            BoplEightPlugin.Log.LogInfo("Expanded online character selection from four to eight visible players.");
        }

        internal static void ExtendSteamFrame(SteamFrame frame, ref int[] currentSquareColors)
        {
            if (frame == null || SteamFrame.IsInitialized || frame.squares == null || frame.squares.Count >= RemotePlayerCapacity)
            {
                return;
            }

            PaletteRuntime.ExtendPlayerColors(frame.playerColors);
            if (frame.squares.Count != 3 || frame.lookingForPlayersCircles == null || frame.lookingForPlayersCircles.Length != 3)
            {
                BoplEightPlugin.Log.LogWarning("BoplEight expected three vanilla Steam avatar slots and left the overlay layout unchanged.");
                return;
            }

            RectTransform selfRect = frame.SelfSquare.GetComponent<RectTransform>();
            var basePositions = new Vector2[4];
            basePositions[0] = selfRect.anchoredPosition;
            for (var index = 0; index < frame.squares.Count; index++)
            {
                basePositions[index + 1] = frame.squares[index].GetComponent<RectTransform>().anchoredPosition;
            }

            float rowSpacing = GetLobbyRowSpacing(selfRect);
            Vector2 firstRowOffset = Vector2.up * (rowSpacing * 0.5f);
            Vector2 secondRowOffset = Vector2.down * (rowSpacing * 0.5f);
            selfRect.anchoredPosition = basePositions[0] + firstRowOffset;
            selfRect.localScale *= RosterLayout.Scale;
            for (var index = 0; index < frame.squares.Count; index++)
            {
                RectTransform squareRect = frame.squares[index].GetComponent<RectTransform>();
                squareRect.anchoredPosition = basePositions[index + 1] + firstRowOffset;
                squareRect.localScale *= RosterLayout.Scale;
            }

            var circles = new List<Image>(frame.lookingForPlayersCircles);
            var circleOffsets = new Vector2[3];
            for (var index = 0; index < frame.lookingForPlayersCircles.Length; index++)
            {
                RectTransform circleRect = frame.lookingForPlayersCircles[index].rectTransform;
                circleOffsets[index] = circleRect.anchoredPosition - basePositions[index + 1];
                circleRect.anchoredPosition = basePositions[index + 1] + firstRowOffset + circleOffsets[index];
                circleRect.localScale *= RosterLayout.Scale;
            }

            for (var column = 0; column < 4; column++)
            {
                int sourceIndex = Math.Min(column, frame.squares.Count - 1);
                SteamFrameButton source = frame.squares[sourceIndex];
                SteamFrameButton clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                clone.name = "BoplEight Steam Slot " + (column + 5);
                RectTransform cloneRect = clone.GetComponent<RectTransform>();
                cloneRect.anchoredPosition = basePositions[column] + secondRowOffset;
                cloneRect.localScale = source.GetComponent<RectTransform>().localScale;
                if (source.image.material != null)
                {
                    clone.image.material = UnityEngine.Object.Instantiate(source.image.material);
                }

                int connectedPlayerIndex = frame.squares.Count;
                clone.button.onClick = new Button.ButtonClickedEvent();
                clone.button.onClick.AddListener(delegate { frame.ClickKickPlayer(connectedPlayerIndex); });
                frame.squares.Add(clone);

                Image circleSource = frame.lookingForPlayersCircles[Math.Min(column, 2)];
                Image circleClone = UnityEngine.Object.Instantiate(circleSource, circleSource.transform.parent);
                circleClone.name = "BoplEight Steam Loading Slot " + (column + 5);
                circleClone.rectTransform.anchoredPosition = basePositions[column] + secondRowOffset + circleOffsets[Math.Min(column, 2)];
                circleClone.rectTransform.localScale = circleSource.rectTransform.localScale;
                circles.Add(circleClone);
            }

            frame.lookingForPlayersCircles = circles.ToArray();
            currentSquareColors = new int[RemotePlayerCapacity];
            for (var index = 0; index < currentSquareColors.Length; index++)
            {
                currentSquareColors[index] = -1;
            }
        }

        [HarmonyPatch(typeof(CharacterSelectHandler_online), "Awake")]
        private static class CharacterSelectAwakePatch
        {
            private static void Prefix()
            {
                BoplEightSession.Clear();
            }

            private static void Postfix(CharacterSelectHandler_online __instance)
            {
                ExtendCharacterSelection(__instance);
            }
        }

        [HarmonyPatch(typeof(SteamFrame), "Awake")]
        private static class SteamFrameAwakePatch
        {
            private static void Prefix(SteamFrame __instance, ref int[] ___currentSquareColors)
            {
                ExtendSteamFrame(__instance, ref ___currentSquareColors);
            }
        }

        [HarmonyPatch(typeof(CSBox_online), "Init")]
        private static class ExpandedRemoteSlotInitPatch
        {
            private static void Prefix(CSBox_online __instance)
            {
                if (__instance != null
                    && RosterLayout.IsExpandedRemoteSlot(__instance.name)
                    && !__instance.gameObject.activeSelf)
                {
                    __instance.gameObject.SetActive(true);
                }
            }
        }

        [HarmonyPatch(typeof(TeamSelector), "Awake")]
        private static class TeamSelectorAwakePatch
        {
            private static void Prefix(TeamSelector __instance)
            {
                PaletteRuntime.ExtendTeamColors(__instance.teams);
            }
        }

        [HarmonyPatch(typeof(TeamSelector), "Select")]
        private static class TeamSelectorSelectPatch
        {
            private static void Prefix(TeamSelector __instance, ref int index)
            {
                if (__instance.teams == null || __instance.teams.Length == 0 || index < 0)
                {
                    return;
                }

                index %= __instance.teams.Length;
            }
        }
    }
}
