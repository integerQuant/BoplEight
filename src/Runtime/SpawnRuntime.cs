using System;
using System.Collections.Generic;
using BoplFixedMath;
using BoplEight.Protocol;
using HarmonyLib;
using UnityEngine;

namespace BoplEight.Runtime
{
    internal static class SpawnRuntime
    {
        internal static void ExtendTeamSpawns(GameSessionHandler session)
        {
            if (session == null || session.teamSpawns == null || session.teamSpawns.Length >= 8)
            {
                return;
            }

            if (session.teamSpawns.Length < 4)
            {
                throw new InvalidOperationException("BoplEight expected each level to provide four vanilla team spawns.");
            }

            Vec2[] original = session.teamSpawns;
            var extended = new Vec2[8];
            Array.Copy(original, extended, original.Length);
            Vec2 safeTeammateOffset = new Vec2(
                session.teammateSpawnSpacing,
                -session.teammateSpawnSpacing * (Fix)0.5f);
            for (var index = 0; index < 4; index++)
            {
                // Vanilla already treats this offset from each authored anchor as a safe teammate spawn.
                extended[index + 4] = original[index] + safeTeammateOffset;
            }

            session.teamSpawns = extended;
        }

        private static int[] DifferentTeams(List<Player> players)
        {
            var teams = new List<int>();
            for (var index = 0; index < players.Count; index++)
            {
                if (!teams.Contains(players[index].Team))
                {
                    teams.Add(players[index].Team);
                }
            }

            teams.Sort();
            return teams.ToArray();
        }

        private static Vec2 PlayerSpawn(Vec2 teamSpawn, int memberIndex, int teamSize, Fix spacing)
        {
            if (teamSize <= 1)
            {
                return teamSpawn;
            }

            Fix angle = Fix.PiTimes2 * (Fix)memberIndex / (Fix)teamSize;
            Fix radius = teamSize == 2 ? spacing * (Fix)0.6f : spacing;
            return teamSpawn + new Vec2(angle) * radius;
        }

        [HarmonyPatch(typeof(GameSessionHandler), "Awake")]
        private static class GameSessionHandlerAwakePatch
        {
            private static void Prefix(GameSessionHandler __instance)
            {
                if (BoplEightSession.ActiveRoster != null)
                {
                    ExtendTeamSpawns(__instance);
                    PaletteRuntime.ExtendTeamColors(__instance.teamColors);
                }
            }
        }

        [HarmonyPatch(typeof(GameSessionHandler), "SpawnPlayers")]
        private static class GameSessionHandlerSpawnPlayersPatch
        {
            private static bool Prefix(
                GameSessionHandler __instance,
                ref bool ___gameInProgress,
                ref bool ___gameOver,
                ref SlimeController[] ___slimeControllers)
            {
                if (BoplEightSession.ActiveRoster == null)
                {
                    return true;
                }

                ___gameInProgress = true;
                ___gameOver = false;
                List<Player> players = PlayerHandler.Get().PlayerList();
                ___slimeControllers = new SlimeController[players.Count];
                int[] differentTeams = DifferentTeams(players);
                var teamSizes = new int[differentTeams.Length];
                var teamMembersSpawned = new int[differentTeams.Length];
                for (var index = 0; index < players.Count; index++)
                {
                    int teamIndex = Array.IndexOf(differentTeams, players[index].Team);
                    if (teamIndex < 0 || teamIndex >= __instance.teamSpawns.Length)
                    {
                        throw new InvalidOperationException("BoplEight could not map a selected team to a level spawn.");
                    }

                    teamSizes[teamIndex]++;
                }

                for (var playerIndex = 0; playerIndex < players.Count; playerIndex++)
                {
                    Player player = players[playerIndex];
                    int teamIndex = Array.IndexOf(differentTeams, player.Team);
                    int memberIndex = teamMembersSpawned[teamIndex]++;
                    player.playersAndClonesStillAlive = 1;
                    Vec2 position = PlayerSpawn(
                        __instance.teamSpawns[teamIndex],
                        memberIndex,
                        teamSizes[teamIndex],
                        __instance.teammateSpawnSpacing);

                    SlimeController slime = FixTransform.InstantiateFixed(__instance.PlayerPrefab, position);
                    ___slimeControllers[playerIndex] = slime;
                    slime.playerNumber = player.Id;
                    slime.transform.SetParent(__instance.playerSpawnsRoot.transform);
                    slime.GetPlayerSprite().sprite = null;
                    slime.GetPlayerSprite().material = player.Color;

                    var abilities = new List<AbilityMonoBehaviour>();
                    player.CurrentAbilities = new List<GameObject>();
                    bool foundOffensiveAbility = false;
                    bool allAbilitiesAreRandom = true;
                    for (var abilityIndex = 0; abilityIndex < player.Abilities.Count; abilityIndex++)
                    {
                        if (player.Abilities[abilityIndex].GetComponent<RandomAbility>() == null)
                        {
                            allAbilitiesAreRandom = false;
                        }
                    }

                    for (var abilityIndex = 0; abilityIndex < player.Abilities.Count; abilityIndex++)
                    {
                        RandomAbility randomAbility = player.Abilities[abilityIndex].GetComponent<RandomAbility>();
                        GameObject abilityObject;
                        if (randomAbility != null)
                        {
                            NamedSprite randomPrefab = RandomAbility.GetRandomAbilityPrefab(
                                randomAbility.abilityIcons,
                                randomAbility.abilityIcons_demo);
                            foundOffensiveAbility |= randomPrefab.isOffensiveAbility;
                            if (allAbilitiesAreRandom && !foundOffensiveAbility && abilityIndex == player.Abilities.Count - 1)
                            {
                                randomPrefab = RandomAbility.GetRandomAbilityPrefab(
                                    randomAbility.abilityIcons,
                                    randomAbility.abilityIcons_demo);
                            }

                            player.AbilityIcons[abilityIndex] = randomPrefab.sprite;
                            abilityObject = FixTransform.InstantiateFixed(randomPrefab.associatedGameObject, Vec2.zero);
                        }
                        else
                        {
                            abilityObject = FixTransform.InstantiateFixed(player.Abilities[abilityIndex], Vec2.zero);
                        }

                        abilityObject.SetActive(false);
                        player.CurrentAbilities.Add(abilityObject);
                        abilities.Add(abilityObject.GetComponent<AbilityMonoBehaviour>());
                    }

                    slime.abilities = abilities;
                    var indicators = new AbilityReadyIndicator[ProtocolConstants.AbilityCount];
                    for (var abilityIndex = 0; abilityIndex < indicators.Length; abilityIndex++)
                    {
                        indicators[abilityIndex] = UnityEngine.Object.Instantiate(
                            __instance.AbilityReadyIndicators[abilityIndex],
                            __instance.playerSpawnsRoot.transform).GetComponent<AbilityReadyIndicator>();
                        indicators[abilityIndex].Init();
                        indicators[abilityIndex].SetColor(__instance.teamColors.teamColors[player.Team].fill);
                        indicators[abilityIndex].GetComponent<FollowTransform>().Leader = slime.transform;
                        indicators[abilityIndex].gameObject.SetActive(false);
                        if (abilityIndex < player.AbilityIcons.Count)
                        {
                            indicators[abilityIndex].SetSprite(player.AbilityIcons[abilityIndex]);
                        }
                    }

                    slime.AbilityReadyIndicators = indicators;
                    if (player.IsLocalPlayer)
                    {
                        InputUpdater inputUpdater = UnityEngine.Object.Instantiate(__instance.InputUpdaterPrefab);
                        UnityEngine.InputSystem.PlayerInput playerInput = inputUpdater.GetComponent<UnityEngine.InputSystem.PlayerInput>();
                        inputUpdater.Claim(player.Id);
                        inputUpdater.Init(player.Id);
                        playerInput.neverAutoSwitchControlSchemes = true;
                        playerInput.ActivateInput();
                    }
                }

                return false;
            }
        }
    }
}
