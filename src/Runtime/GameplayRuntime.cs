using System;
using System.Collections.Generic;
using System.Reflection;
using BoplEight.Protocol;
using BoplEight.Ui;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace BoplEight.Runtime
{
    internal static class GameplayRuntime
    {
        private static readonly FieldInfo KillsThisFrameField = AccessTools.Field(typeof(AchievementHandler), "killsThisFrame");
        private static readonly FieldInfo TeleportPlayerBufferField = AccessTools.Field(typeof(TeleportIndicator), "playerObjectsBuffer");
        private static readonly FieldInfo TeleportIndicatorBufferField = AccessTools.Field(typeof(TeleportIndicator), "indicatorObjectsBuffer");
        private static readonly FieldInfo MagnetHitBufferField = AccessTools.Field(typeof(MagnetGun), "hitBuffer");
        private const int ExpandedPhysicsHitCapacity = 256;

        private static Image[] ExtendWinnerImages(Image[] images, int capacity, string namePrefix)
        {
            if (images == null || images.Length == 0 || images.Length >= capacity)
            {
                return images;
            }

            var extended = new Image[capacity];
            Array.Copy(images, extended, images.Length);
            Image source = images[images.Length - 1];
            RectTransform sourceRect = source.rectTransform;
            float spacing = Math.Max(24f, sourceRect.rect.width * 0.7f);
            for (var index = images.Length; index < capacity; index++)
            {
                Image clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                clone.name = namePrefix + " " + (index + 1);
                clone.rectTransform.anchoredPosition = sourceRect.anchoredPosition + Vector2.right * spacing * (index - images.Length + 1);
                extended[index] = clone;
            }

            return extended;
        }

        private static void LayoutWinnerImages(Image[] images, int visibleCount)
        {
            if (images == null || images.Length == 0 || visibleCount <= 0)
            {
                return;
            }

            visibleCount = Math.Min(visibleCount, images.Length);
            Vector2 center = Vector2.zero;
            for (var index = 0; index < visibleCount; index++)
            {
                center += images[index].rectTransform.anchoredPosition;
            }

            center /= visibleCount;
            const int columns = 4;
            float horizontalSpacing = Math.Max(24f, images[0].rectTransform.rect.width * 0.68f);
            float verticalSpacing = Math.Max(24f, images[0].rectTransform.rect.height * 0.58f);
            int rowCount = (visibleCount + columns - 1) / columns;
            for (var index = 0; index < images.Length; index++)
            {
                bool visible = index < visibleCount;
                images[index].gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                int row = index / columns;
                int column = index % columns;
                int entriesInRow = Math.Min(columns, visibleCount - row * columns);
                float x = (column - (entriesInRow - 1) * 0.5f) * horizontalSpacing;
                float y = ((rowCount - 1) * 0.5f - row) * verticalSpacing;
                images[index].rectTransform.anchoredPosition = center + new Vector2(x, y);
                images[index].rectTransform.localScale = Vector3.one * 0.72f;
            }
        }

        [HarmonyPatch(typeof(Mine), "Awake")]
        private static class MineAwakePatch
        {
            private static void Prefix(ref PhysicsParent[] ___scanHitsBuffer)
            {
                if (BoplEightSession.ActiveRoster != null && (___scanHitsBuffer == null || ___scanHitsBuffer.Length < 136))
                {
                    ___scanHitsBuffer = new PhysicsParent[ProtocolConstants.MaximumPlayers * (Constants.MaxClones + 1)];
                }
            }
        }

        [HarmonyPatch(typeof(TeleportIndicator), "Awake")]
        private static class TeleportIndicatorAwakePatch
        {
            private static void Prefix()
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    EnsureStaticPhysicsBuffer(TeleportPlayerBufferField);
                    EnsureStaticPhysicsBuffer(TeleportIndicatorBufferField);
                }
            }
        }

        [HarmonyPatch(typeof(MagnetGun), "Awake")]
        private static class MagnetGunAwakePatch
        {
            private static void Prefix()
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    EnsureStaticPhysicsBuffer(MagnetHitBufferField);
                }
            }
        }

        [HarmonyPatch(typeof(Shockwave), "Awake")]
        private static class ShockwaveAwakePatch
        {
            private static void Prefix(ref PhysicsParent[] ___hits)
            {
                if (BoplEightSession.ActiveRoster != null && (___hits == null || ___hits.Length < ExpandedPhysicsHitCapacity))
                {
                    ___hits = new PhysicsParent[ExpandedPhysicsHitCapacity];
                }
            }
        }

        [HarmonyPatch(typeof(Roll), "Awake")]
        private static class RollAwakePatch
        {
            private static void Prefix(ref PhysicsParent[] ___collidersBuffer)
            {
                if (BoplEightSession.ActiveRoster != null
                    && (___collidersBuffer == null || ___collidersBuffer.Length < ExpandedPhysicsHitCapacity))
                {
                    ___collidersBuffer = new PhysicsParent[ExpandedPhysicsHitCapacity];
                }
            }
        }

        private static void EnsureStaticPhysicsBuffer(FieldInfo field)
        {
            if (field == null)
            {
                return;
            }

            var buffer = (PhysicsParent[])field.GetValue(null);
            if (buffer == null || buffer.Length < ExpandedPhysicsHitCapacity)
            {
                field.SetValue(null, new PhysicsParent[ExpandedPhysicsHitCapacity]);
            }
        }

        [HarmonyPatch(typeof(AbilitySelectController), "Awake")]
        private static class AbilitySelectControllerAwakePatch
        {
            private static void Prefix(AbilitySelectController __instance)
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    PaletteRuntime.ExtendTeamColors(__instance.teamColors);
                }
            }
        }

        [HarmonyPatch(typeof(AbilitySelectCircle), "SetPlayer")]
        private static class AbilitySelectCircleSetPlayerPatch
        {
            private static void Prefix(TeamColors ___teamColors)
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    PaletteRuntime.ExtendTeamColors(___teamColors);
                }
            }
        }

        [HarmonyPatch(typeof(ReviveEffect), "Awake")]
        private static class ReviveEffectAwakePatch
        {
            private static void Prefix(ReviveEffect __instance)
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    PaletteRuntime.ExtendTeamColors(__instance.teamColors);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerCollision), "Awake")]
        private static class PlayerCollisionAwakePatch
        {
            private static void Prefix(PlayerCollision __instance)
            {
                if (BoplEightSession.ActiveRoster != null && __instance.reviveEffectPrefab != null)
                {
                    PaletteRuntime.ExtendTeamColors(__instance.reviveEffectPrefab.teamColors);
                }
            }
        }

        [HarmonyPatch(typeof(AchievementHandler), "RegisterKill")]
        private static class AchievementRegisterKillPatch
        {
            private static bool Prefix(int playerId)
            {
                if (BoplEightSession.ActiveRoster == null)
                {
                    return true;
                }

                if (KillsThisFrameField == null)
                {
                    return true;
                }

                var kills = (int[])KillsThisFrameField.GetValue(null);
                if (kills == null || kills.Length < ProtocolConstants.MaximumPlayers)
                {
                    var extended = new int[ProtocolConstants.MaximumPlayers];
                    if (kills != null)
                    {
                        Array.Copy(kills, extended, kills.Length);
                    }

                    kills = extended;
                    KillsThisFrameField.SetValue(null, kills);
                }

                if (playerId >= 1 && playerId <= ProtocolConstants.MaximumPlayers)
                {
                    kills[playerId - 1]++;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterStatsList), "OnEnable")]
        private static class CharacterStatsListOnEnablePatch
        {
            private static void Prefix(CharacterStatsList __instance, ref float ___DelaySpacing, out float __state)
            {
                __state = ___DelaySpacing;
                if (BoplEightSession.ActiveRoster != null)
                {
                    ___DelaySpacing = RosterLayout.FittedRoundSummaryDelaySpacing(
                        ___DelaySpacing,
                        PlayerHandler.Get().NumberOfPlayers());
                    __instance.winnersOfDraw = ExtendWinnerImages(
                        __instance.winnersOfDraw,
                        ProtocolConstants.MaximumPlayers,
                        "BoplEight Draw Winner");
                    __instance.teams3v1WinnersRef = ExtendWinnerImages(
                        __instance.teams3v1WinnersRef,
                        ProtocolConstants.MaximumPlayers,
                        "BoplEight Team Winner");
                }
            }

            private static void Postfix(CharacterStatsList __instance, ref float ___DelaySpacing, float __state)
            {
                ___DelaySpacing = __state;
                if (BoplEightSession.ActiveRoster == null)
                {
                    return;
                }

                var winners = new List<Player>();
                List<Player> players = PlayerHandler.Get().PlayerList();
                bool multipleTeams = false;
                int firstTeam = -1;
                for (var index = 0; index < players.Count; index++)
                {
                    if (!players[index].IsMostRecentWinner)
                    {
                        continue;
                    }

                    winners.Add(players[index]);
                    if (firstTeam < 0)
                    {
                        firstTeam = players[index].Team;
                    }
                    else if (players[index].Team != firstTeam)
                    {
                        multipleTeams = true;
                    }
                }

                if (multipleTeams)
                {
                    LayoutWinnerImages(__instance.winnersOfDraw, winners.Count);
                }
                else if (winners.Count > 3)
                {
                    for (var index = 0; index < winners.Count && index < __instance.teams3v1WinnersRef.Length; index++)
                    {
                        __instance.teams3v1WinnersRef[index].material = winners[index].Color;
                    }

                    LayoutWinnerImages(__instance.teams3v1WinnersRef, winners.Count);
                }
            }
        }

        [HarmonyPatch(typeof(HandleAbilitySelectUI), "Init")]
        private static class HandleAbilitySelectUiInitPatch
        {
            private static void Prefix(ref float ___Separation, out float __state)
            {
                __state = ___Separation;
                int playerCount = PlayerHandler.Get().NumberOfPlayers();
                if (BoplEightSession.ActiveRoster != null && playerCount > 4)
                {
                    ___Separation = RosterLayout.FittedSeparation(___Separation, playerCount);
                }
            }

            private static void Postfix(AbilitySelectCircle[] ___Selectors, ref float ___Separation, float __state)
            {
                ___Separation = __state;
                if (BoplEightSession.ActiveRoster == null || ___Selectors == null || ___Selectors.Length <= 4)
                {
                    return;
                }

                for (var index = 0; index < ___Selectors.Length; index++)
                {
                    Vector3 scale = ___Selectors[index].trans.localScale;
                    scale.x = RosterLayout.FittedScale(scale.x);
                    scale.y = RosterLayout.FittedScale(scale.y);
                    ___Selectors[index].trans.localScale = scale;
                }
            }
        }

        [HarmonyPatch(typeof(CSBox_online), "Init")]
        private static class CSBoxInitPatch
        {
            private static void Postfix(SteamConnection targetPlayer)
            {
                if (targetPlayer == null || targetPlayer.lobby_isReady || SteamManager.instance == null)
                {
                    return;
                }

                byte memberIndex = 0;
                foreach (Friend member in SteamManager.instance.currentLobby.Members)
                {
                    if ((ulong)member.Id == (ulong)targetPlayer.id)
                    {
                        targetPlayer.lobby_team = memberIndex;
                        return;
                    }

                    memberIndex++;
                }
            }
        }

        [HarmonyPatch(typeof(SteamFrame), "ClickKickPlayer")]
        private static class SteamFrameClickKickPlayerPatch
        {
            private static bool Prefix(ref int index)
            {
                if (SteamManager.instance == null
                    || index < 0
                    || index >= SteamManager.instance.connectedPlayers.Count)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
