using System.Collections.Generic;
using System.Reflection;
using BoplEight.Protocol;
using BoplEight.Ui;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoplEight.Runtime
{
    internal static class LobbyUiRuntime
    {
        private const int RemotePlayerCapacity = 7;
        private static readonly FieldInfo AnimatedTransformsField = AccessTools.Field(typeof(AnimateInOutUI), "animatedTransforms");
        private static readonly FieldInfo StartHeightField = AccessTools.Field(typeof(AnimateInOutUI), "startHeight");
        private static readonly FieldInfo EndHeightField = AccessTools.Field(typeof(AnimateInOutUI), "endHeight");
        private static readonly FieldInfo OriginalHeightsField = AccessTools.Field(typeof(AnimateInOutUI), "originalHeights");
        private static readonly FieldInfo HorizontalInsteadField = AccessTools.Field(typeof(AnimateInOutUI), "horizontalInstead");
        private static readonly FieldInfo AutoCallAnimateInOnStartField = AccessTools.Field(typeof(AnimateInOutUI), "autoCallAnimateInOnStart");
        private static readonly FieldInfo AnimatingInField = AccessTools.Field(typeof(AnimateInOutUI), "animatingIn");
        private static readonly FieldInfo TimeSinceAnimateInField = AccessTools.Field(typeof(AnimateInOutUI), "timeSinceAnimateIn");
        private static readonly HashSet<int> ExpandedAnimationTravel = new HashSet<int>();

        private static float[] GetEightColumnCenters(Vector2[] vanillaPositions)
        {
            var centers = new float[ProtocolConstants.MaximumPlayers];
            for (var index = 0; index < centers.Length; index++)
            {
                centers[index] = RosterLayout.ColumnCenter(
                    vanillaPositions[0].x,
                    vanillaPositions[vanillaPositions.Length - 1].x,
                    index,
                    centers.Length);
            }

            return centers;
        }

        private static void FitScale(RectTransform rect)
        {
            Vector3 scale = rect.localScale;
            scale.x = RosterLayout.FittedScale(scale.x);
            scale.y = RosterLayout.FittedScale(scale.y);
            rect.localScale = scale;
        }

        private static void ExpandCharacterAnimationTravel(Transform cardRoot)
        {
            if (AnimatedTransformsField == null
                || StartHeightField == null
                || EndHeightField == null
                || OriginalHeightsField == null
                || HorizontalInsteadField == null
                || AutoCallAnimateInOnStartField == null)
            {
                return;
            }

            AnimateInOutUI[] animations = cardRoot.GetComponentsInChildren<AnimateInOutUI>(true);
            for (var index = 0; index < animations.Length; index++)
            {
                AnimateInOutUI animation = animations[index];
                if (animation == null
                    || !ExpandedAnimationTravel.Add(animation.GetInstanceID())
                    || (bool)HorizontalInsteadField.GetValue(animation))
                {
                    continue;
                }

                var transforms = (RectTransform[])AnimatedTransformsField.GetValue(animation);
                if (transforms == null || transforms.Length == 0)
                {
                    continue;
                }

                bool movesCardRoot = false;
                bool movesDescendant = false;
                for (var transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    RectTransform target = transforms[transformIndex];
                    if (target == cardRoot)
                    {
                        movesCardRoot = true;
                    }
                    else if (target != null && target.IsChildOf(cardRoot))
                    {
                        movesDescendant = true;
                    }
                }

                if (movesCardRoot || !movesDescendant)
                {
                    continue;
                }

                if (!RosterLayout.ShouldSetInitialAnimationPosition(
                    (bool)AutoCallAnimateInOnStartField.GetValue(animation)))
                {
                    continue;
                }

                var originalHeights = (float[])OriginalHeightsField.GetValue(animation);
                float startHeight = (float)StartHeightField.GetValue(animation);
                float endHeight = (float)EndHeightField.GetValue(animation);
                float minimumRestingPosition = transforms[0].anchoredPosition.y;
                float maximumRestingPosition = minimumRestingPosition;
                if (originalHeights != null && originalHeights.Length > 0)
                {
                    minimumRestingPosition = originalHeights[0];
                    maximumRestingPosition = originalHeights[0];
                    for (var heightIndex = 1; heightIndex < originalHeights.Length; heightIndex++)
                    {
                        minimumRestingPosition = Mathf.Min(minimumRestingPosition, originalHeights[heightIndex]);
                        maximumRestingPosition = Mathf.Max(maximumRestingPosition, originalHeights[heightIndex]);
                    }
                }

                StartHeightField.SetValue(
                    animation,
                    RosterLayout.InitialAnimationPosition(
                        startHeight,
                        startHeight >= minimumRestingPosition ? minimumRestingPosition : maximumRestingPosition));
                EndHeightField.SetValue(
                    animation,
                    RosterLayout.FittedAnimationBoundary(
                        endHeight,
                        endHeight >= minimumRestingPosition ? minimumRestingPosition : maximumRestingPosition));

                float fittedStartHeight = (float)StartHeightField.GetValue(animation);
                for (var transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    RectTransform target = transforms[transformIndex];
                    if (target != null && target != cardRoot && target.IsChildOf(cardRoot))
                    {
                        target.anchoredPosition = new Vector2(target.anchoredPosition.x, fittedStartHeight);
                    }
                }
            }
        }

        private static void CopyAnimationBaselines(Transform sourceRoot, Transform cloneRoot)
        {
            if (OriginalHeightsField == null)
            {
                return;
            }

            AnimateInOutUI[] sourceAnimations = sourceRoot.GetComponentsInChildren<AnimateInOutUI>(true);
            AnimateInOutUI[] cloneAnimations = cloneRoot.GetComponentsInChildren<AnimateInOutUI>(true);
            int count = Mathf.Min(sourceAnimations.Length, cloneAnimations.Length);
            for (var index = 0; index < count; index++)
            {
                var originalHeights = (float[])OriginalHeightsField.GetValue(sourceAnimations[index]);
                if (originalHeights != null)
                {
                    OriginalHeightsField.SetValue(cloneAnimations[index], (float[])originalHeights.Clone());
                }
            }
        }

        private static void ShowInitialJoinControl(CharacterSelectHandler_online handler)
        {
            if (handler == null
                || handler.characterSelectBox == null
                || handler.characterSelectBox.animateJoin == null
                || AnimatedTransformsField == null
                || OriginalHeightsField == null
                || AnimatingInField == null
                || TimeSinceAnimateInField == null)
            {
                return;
            }

            AnimateInOutUI animation = handler.characterSelectBox.animateJoin;
            var transforms = (RectTransform[])AnimatedTransformsField.GetValue(animation);
            var originalHeights = (float[])OriginalHeightsField.GetValue(animation);
            if (transforms == null || originalHeights == null || transforms.Length != originalHeights.Length)
            {
                return;
            }

            for (var index = 0; index < transforms.Length; index++)
            {
                RectTransform target = transforms[index];
                if (target != null)
                {
                    target.anchoredPosition = new Vector2(target.anchoredPosition.x, originalHeights[index]);
                }
            }

            AnimatingInField.SetValue(animation, true);
            TimeSinceAnimateInField.SetValue(animation, float.MaxValue);
        }

        private static void AssignUniqueAvatarMaterial(SteamFrameButton square, string name)
        {
            if (square == null || square.image == null || square.image.material == null)
            {
                return;
            }

            Material material = UnityEngine.Object.Instantiate(square.image.material);
            material.name = name;
            square.image.material = material;
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

            float[] slotCenters = GetEightColumnCenters(basePositions);
            float localRootY = RosterLayout.FittedRootPosition(basePositions[0].y, basePositions[1].y);
            localRect.anchoredPosition = new Vector2(slotCenters[0], localRootY);
            FitScale(localRect);
            ExpandCharacterAnimationTravel(localRect);
            ShowInitialJoinControl(handler);

            var boxes = new List<CSBox_online>(RemotePlayerCapacity);
            for (var index = 0; index < originalBoxes.Length; index++)
            {
                RectTransform boxRect = originalBoxes[index].GetComponent<RectTransform>();
                boxRect.anchoredPosition = new Vector2(slotCenters[index + 1], basePositions[index + 1].y);
                FitScale(boxRect);
                boxes.Add(originalBoxes[index]);
            }

            for (var slot = 4; slot < ProtocolConstants.MaximumPlayers; slot++)
            {
                CSBox_online source = originalBoxes[(slot - 4) % originalBoxes.Length];
                CSBox_online clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                CopyAnimationBaselines(source.transform, clone.transform);
                clone.gameObject.SetActive(false);

                clone.name = "BoplEight Remote Slot " + (slot + 1);
                clone.connectedPlayer = null;
                clone.isVisible = false;
                RectTransform cloneRect = clone.GetComponent<RectTransform>();
                int sourceIndex = (slot - 4) % originalBoxes.Length;
                cloneRect.anchoredPosition = new Vector2(slotCenters[slot], basePositions[sourceIndex + 1].y);
                cloneRect.localScale = source.GetComponent<RectTransform>().localScale;
                ExpandCharacterAnimationTravel(cloneRect);
                boxes.Add(clone);
            }

            for (var index = 0; index < originalBoxes.Length; index++)
            {
                ExpandCharacterAnimationTravel(originalBoxes[index].transform);
            }

            var circles = new List<Image>(RemotePlayerCapacity);
            var circleOffsets = new Vector2[3];
            for (var index = 0; index < originalCircles.Length; index++)
            {
                RectTransform circleRect = originalCircles[index].rectTransform;
                circleOffsets[index] = circleRect.anchoredPosition - basePositions[index + 1];
                circleRect.anchoredPosition = new Vector2(slotCenters[index + 1], basePositions[index + 1].y) + circleOffsets[index];
                FitScale(circleRect);
                circles.Add(originalCircles[index]);
            }

            for (var slot = 4; slot < ProtocolConstants.MaximumPlayers; slot++)
            {
                int sourceIndex = (slot - 4) % originalCircles.Length;
                Image source = originalCircles[sourceIndex];
                Image clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                clone.name = "BoplEight Loading Slot " + (slot + 1);
                clone.rectTransform.anchoredPosition = new Vector2(slotCenters[slot], basePositions[sourceIndex + 1].y) + circleOffsets[sourceIndex];
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

            float[] slotCenters = GetEightColumnCenters(basePositions);
            SteamFrameButton[] originalSquares = frame.squares.ToArray();
            Image[] originalCircles = frame.lookingForPlayersCircles;
            selfRect.anchoredPosition = new Vector2(slotCenters[0], basePositions[0].y);
            FitScale(selfRect);
            AssignUniqueAvatarMaterial(frame.SelfSquare, "BoplEight Steam Avatar Material 1");
            for (var index = 0; index < originalSquares.Length; index++)
            {
                RectTransform squareRect = originalSquares[index].GetComponent<RectTransform>();
                squareRect.anchoredPosition = new Vector2(slotCenters[index + 1], basePositions[index + 1].y);
                FitScale(squareRect);
                AssignUniqueAvatarMaterial(originalSquares[index], "BoplEight Steam Avatar Material " + (index + 2));
            }

            var circles = new List<Image>(originalCircles);
            var circleOffsets = new Vector2[3];
            for (var index = 0; index < originalCircles.Length; index++)
            {
                RectTransform circleRect = originalCircles[index].rectTransform;
                circleOffsets[index] = circleRect.anchoredPosition - basePositions[index + 1];
                circleRect.anchoredPosition = new Vector2(slotCenters[index + 1], basePositions[index + 1].y) + circleOffsets[index];
                FitScale(circleRect);
            }

            for (var slot = 4; slot < ProtocolConstants.MaximumPlayers; slot++)
            {
                int sourceIndex = (slot - 4) % originalSquares.Length;
                SteamFrameButton source = originalSquares[sourceIndex];
                SteamFrameButton clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                clone.name = "BoplEight Steam Slot " + (slot + 1);
                RectTransform cloneRect = clone.GetComponent<RectTransform>();
                cloneRect.anchoredPosition = new Vector2(slotCenters[slot], basePositions[sourceIndex + 1].y);
                cloneRect.localScale = source.GetComponent<RectTransform>().localScale;
                AssignUniqueAvatarMaterial(clone, "BoplEight Steam Avatar Material " + (slot + 1));

                int connectedPlayerIndex = slot - 1;
                clone.button.onClick = new Button.ButtonClickedEvent();
                clone.button.onClick.AddListener(delegate { frame.ClickKickPlayer(connectedPlayerIndex); });
                frame.squares.Add(clone);

                Image circleSource = originalCircles[sourceIndex];
                Image circleClone = UnityEngine.Object.Instantiate(circleSource, circleSource.transform.parent);
                circleClone.name = "BoplEight Steam Loading Slot " + (slot + 1);
                circleClone.rectTransform.anchoredPosition = new Vector2(slotCenters[slot], basePositions[sourceIndex + 1].y) + circleOffsets[sourceIndex];
                circleClone.rectTransform.localScale = circleSource.rectTransform.localScale;
                circles.Add(circleClone);
            }

            frame.lookingForPlayersCircles = circles.ToArray();
            currentSquareColors = new int[RemotePlayerCapacity];
            for (var index = 0; index < currentSquareColors.Length; index++)
            {
                currentSquareColors[index] = -1;
            }

            BoplEightPlugin.Log.LogInfo("Expanded the Steam avatar overlay from four to eight visible players.");
        }

        internal static void RefreshSteamAvatars(SteamFrame frame)
        {
            if (frame == null
                || SteamManager.instance == null
                || frame.squares == null
                || frame.squares.Count < RemotePlayerCapacity)
            {
                return;
            }

            List<SteamConnection> connections = SteamManager.instance.connectedPlayers;
            for (var squareIndex = 0; squareIndex < frame.squares.Count; squareIndex++)
            {
                bool hasAvatar = squareIndex < connections.Count
                    && connections[squareIndex].hasAvatar
                    && connections[squareIndex].avatar != null;
                int connectionIndex = AvatarSlotMapping.ConnectionIndexForSquare(
                    squareIndex,
                    connections.Count,
                    hasAvatar);
                SteamFrameButton square = frame.squares[squareIndex];
                bool visible = connectionIndex >= 0;
                square.gameObject.SetActive(visible);
                if (visible)
                {
                    SteamConnection connection = connections[connectionIndex];
                    Material material = square.image.material;
                    if (material != null)
                    {
                        material.SetTexture(
                            "_ProfileTexture",
                            Settings.Get().Hide == 2 ? Texture2D.blackTexture : connection.avatar);
                    }

                    Material renderedMaterial = square.image.materialForRendering;
                    if (renderedMaterial != null && renderedMaterial.HasProperty("_Blue"))
                    {
                        Color color = Color.white;
                        if (connection.lobby_isReady
                            && connection.lobby_color >= 0
                            && frame.playerColors != null
                            && connection.lobby_color < frame.playerColors.Length)
                        {
                            Material playerMaterial = frame.playerColors[connection.lobby_color].playerMaterial;
                            if (playerMaterial != null && playerMaterial.HasProperty("_ShadowColor"))
                            {
                                color = playerMaterial.GetColor("_ShadowColor");
                            }
                        }

                        renderedMaterial.SetColor("_Blue", color);
                    }
                }

                if (frame.lookingForPlayersCircles != null
                    && squareIndex < frame.lookingForPlayersCircles.Length)
                {
                    frame.lookingForPlayersCircles[squareIndex].enabled = !visible && SteamManager.currentlyLookingForPlayers;
                }
            }
        }

        [HarmonyPatch(typeof(CharacterSelectHandler_online), "Awake")]
        private static class CharacterSelectAwakePatch
        {
            private static void Prefix()
            {
                BoplEightSession.Clear();
                ExpandedAnimationTravel.Clear();
            }

        }

        [HarmonyPatch(typeof(CharacterSelectHandler_online), "Start")]
        private static class CharacterSelectStartPatch
        {
            private static void Postfix(CharacterSelectHandler_online __instance)
            {
                ExtendCharacterSelection(__instance);
            }
        }

        [HarmonyPatch(typeof(AnimateInOutUI), "Start")]
        private static class InitialJoinAnimationStartPatch
        {
            private static void Postfix(AnimateInOutUI __instance)
            {
                CharacterSelectHandler_online handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
                if (handler != null
                    && handler.characterSelectBox != null
                    && __instance == handler.characterSelectBox.animateJoin
                    && handler.networkPlayerBoxes != null
                    && handler.networkPlayerBoxes.Length >= RemotePlayerCapacity)
                {
                    ShowInitialJoinControl(handler);
                }
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

        [HarmonyPatch(typeof(SteamFrame), "Update")]
        private static class SteamFrameUpdatePatch
        {
            private static void Postfix(SteamFrame __instance)
            {
                RefreshSteamAvatars(__instance);
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
