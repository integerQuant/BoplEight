using System;
using System.Collections.Generic;
using System.Reflection;
using BoplEight.Protocol;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoplEight.Runtime
{
    internal static class RosterRuntime
    {
        private static readonly FieldInfo NextStartGameSequenceField = AccessTools.Field(typeof(SteamManager), "nextStartGameSeq");
        private static bool suppressNextForceStart;
        private static bool abortNextLevelLoad;

        internal static void Reset()
        {
            suppressNextForceStart = false;
            abortNextLevelLoad = false;
        }

        internal static bool TryValidateRoster(StartRoster roster, ulong senderSteamId, out string reason)
        {
            if (!ValidateRosterAgainstLobby(roster, senderSteamId, out reason))
            {
                return false;
            }

            if (!ValidateRosterAssets(roster, out reason))
            {
                return false;
            }

            if (!ValidateRosterConnections(roster, out reason))
            {
                return false;
            }

            return true;
        }

        internal static bool TryActivateRoster(StartRoster roster, ulong senderSteamId, out string reason)
        {
            if (!TryValidateRoster(roster, senderSteamId, out reason))
            {
                return false;
            }

            if (!BoplEightSession.TryActivateStartRoster(roster, senderSteamId, out reason))
            {
                return false;
            }

            MirrorVanillaStartParameters(roster);
            return true;
        }

        internal static void ApplyAcceptedRoster()
        {
            if (GameSession.inMenus)
            {
                CharacterSelectHandler_online.ForceStartGame();
            }
            else
            {
                SteamManager.ForceLoadNextLevel();
            }
        }

        internal static PlayerDescriptor[] SortedPlayers(StartRoster roster)
        {
            var players = (PlayerDescriptor[])roster.Players.Clone();
            Array.Sort(players, delegate(PlayerDescriptor left, PlayerDescriptor right)
            {
                return left.Slot.CompareTo(right.Slot);
            });
            return players;
        }

        internal static PlayerDescriptor FindBySteamId(StartRoster roster, ulong steamId)
        {
            if (roster == null)
            {
                return null;
            }

            for (var index = 0; index < roster.Players.Length; index++)
            {
                if (roster.Players[index].SteamId == steamId)
                {
                    return roster.Players[index];
                }
            }

            return null;
        }

        internal static PlayerDescriptor FindByPlayerId(StartRoster roster, int playerId)
        {
            if (roster == null || playerId < 1 || playerId > ProtocolConstants.MaximumPlayers)
            {
                return null;
            }

            byte slot = (byte)(playerId - 1);
            for (var index = 0; index < roster.Players.Length; index++)
            {
                if (roster.Players[index].Slot == slot)
                {
                    return roster.Players[index];
                }
            }

            return null;
        }

        internal static SteamConnection FindConnection(ulong steamId)
        {
            if (SteamManager.instance == null)
            {
                return null;
            }

            List<SteamConnection> connections = SteamManager.instance.connectedPlayers;
            for (var index = 0; index < connections.Count; index++)
            {
                if ((ulong)connections[index].id == steamId)
                {
                    return connections[index];
                }
            }

            return null;
        }

        private static StartRoster BuildInitialRoster(SteamManager manager, PlayerInit hostPlayer)
        {
            var connections = new List<SteamConnection>();
            for (var index = 0; index < manager.connectedPlayers.Count; index++)
            {
                if (manager.connectedPlayers[index].Connected)
                {
                    connections.Add(manager.connectedPlayers[index]);
                }
            }
            connections.Sort(delegate(SteamConnection left, SteamConnection right)
            {
                return ((ulong)left.id).CompareTo((ulong)right.id);
            });

            int playerCount = connections.Count + 1;
            if (playerCount < ProtocolConstants.MinimumPlayers || playerCount > ProtocolConstants.MaximumPlayers)
            {
                throw new InvalidOperationException("BoplEight requires between two and eight lobby members before starting.");
            }

            var players = new PlayerDescriptor[playerCount];
            players[0] = new PlayerDescriptor(
                0,
                (ulong)SteamClient.SteamId,
                CheckedPlayerColorIndex(hostPlayer.color, "host player color"),
                CheckedTeamIndex(hostPlayer.team, "host team"),
                hostPlayer.usesKeyboardMouse,
                new byte[] { (byte)hostPlayer.ability0, (byte)hostPlayer.ability1, (byte)hostPlayer.ability2 });

            byte demoMask = (byte)(manager.dlc.HasDLC() ? 1 : 0);
            for (var index = 0; index < connections.Count; index++)
            {
                SteamConnection connection = connections[index];
                byte slot = (byte)(index + 1);
                players[index + 1] = new PlayerDescriptor(
                    slot,
                    (ulong)connection.id,
                    CheckedPlayerColorIndex(connection.lobby_color, "remote player color"),
                    CheckedTeamIndex(connection.lobby_team, "remote team"),
                    connection.lobby_usesKeyboardAndMouse,
                    new byte[] { connection.lobby_ability1, connection.lobby_ability2, connection.lobby_ability3 });

                if (connection.ownsFullGame)
                {
                    demoMask = (byte)(demoMask | (1 << slot));
                }
            }

            var settings = new MatchStartSettings(
                NextSequence(manager),
                (uint)Environment.TickCount,
                (byte)Settings.Get().NumberOfAbilities,
                GameSession.CurrentLevel(),
                (byte)Math.Max(Host.MinInputDelayBufferSize, Math.Min(Host.MaxInputDelayBufferSize, manager.startFrameBuffer)),
                demoMask);
            return new StartRoster(settings, players);
        }

        private static StartRoster BuildNextLevelRoster(SteamManager manager, StartRoster currentRoster)
        {
            PlayerDescriptor[] currentPlayers = SortedPlayers(currentRoster);
            var players = new List<PlayerDescriptor>(currentPlayers.Length);
            List<Player> gamePlayers = PlayerHandler.Get().PlayerList();
            NamedSpriteList abilityIcons = manager.abilityIcons;
            byte abilityCount = (byte)Settings.Get().NumberOfAbilities;
            byte demoMask = 0;

            for (var index = 0; index < currentPlayers.Length; index++)
            {
                PlayerDescriptor current = currentPlayers[index];
                bool isLocal = current.SteamId == (ulong)SteamClient.SteamId;
                SteamConnection connection = isLocal ? null : FindConnection(current.SteamId);
                if (!isLocal && (connection == null || !connection.Connected))
                {
                    continue;
                }

                Player player = PlayerHandler.Get().GetPlayer(current.Slot + 1);
                byte[] abilities = (byte[])current.AbilityIds.Clone();
                if (isLocal && player != null)
                {
                    for (var abilityIndex = 0; abilityIndex < abilityCount && abilityIndex < player.Abilities.Count; abilityIndex++)
                    {
                        int selectedAbility = abilityIcons.IndexOf(player.Abilities[abilityIndex].name);
                        if (selectedAbility >= 0 && selectedAbility <= byte.MaxValue)
                        {
                            abilities[abilityIndex] = (byte)selectedAbility;
                        }
                    }
                }
                else if (connection != null)
                {
                    abilities[0] = connection.lobby_ability1;
                    abilities[1] = connection.lobby_ability2;
                    abilities[2] = connection.lobby_ability3;
                }

                bool usesKeyboardAndMouse = isLocal
                    ? (player != null && player.UsesKeyboardAndMouse)
                    : connection.lobby_usesKeyboardAndMouse;
                players.Add(new PlayerDescriptor(
                    current.Slot,
                    current.SteamId,
                    current.PlayerColorId,
                    current.TeamId,
                    usesKeyboardAndMouse,
                    abilities));

                bool ownsFullGame = isLocal ? manager.dlc.HasDLC() : connection.ownsFullGame;
                if (ownsFullGame)
                {
                    demoMask = (byte)(demoMask | (1 << current.Slot));
                }
            }

            if (players.Count < ProtocolConstants.MinimumPlayers)
            {
                throw new InvalidOperationException("A BoplEight next-level roster needs at least two connected players.");
            }

            var settings = new MatchStartSettings(
                NextSequence(manager),
                (uint)Environment.TickCount,
                abilityCount,
                GameSession.CurrentLevel(),
                (byte)Math.Max(Host.MinInputDelayBufferSize, Math.Min(Host.MaxInputDelayBufferSize, Host.CurrentDelayBufferSize)),
                demoMask);
            return new StartRoster(settings, players.ToArray());
        }

        internal static bool BroadcastRoster(StartRoster roster)
        {
            bool allSent = true;
            byte[] packet = PacketCodec.EncodeStartRoster(roster);
            for (var index = 0; index < SteamManager.instance.connectedPlayers.Count; index++)
            {
                SteamConnection connection = SteamManager.instance.connectedPlayers[index];
                if (connection.Connected)
                {
                    Result result = connection.Connection.SendMessage(packet, SendType.Reliable);
                    if (result != Result.OK)
                    {
                        allSent = false;
                        BoplEightPlugin.Log.LogWarning("Steam rejected a BoplEight roster prepare message for peer " + connection.id + ": " + result + ".");
                    }
                }
                else
                {
                    allSent = false;
                }
            }

            return allSent;
        }

        private static bool ValidateRosterAgainstLobby(StartRoster roster, ulong senderSteamId, out string reason)
        {
            if (roster == null || SteamManager.instance == null)
            {
                reason = "The game lobby is unavailable for roster validation.";
                return false;
            }

            var members = new HashSet<ulong>();
            foreach (Friend member in SteamManager.instance.currentLobby.Members)
            {
                members.Add((ulong)member.Id);
            }

            if (members.Count != roster.Players.Length)
            {
                reason = "The start roster does not match the current Steam lobby member count.";
                return false;
            }

            if (!members.Contains(senderSteamId))
            {
                reason = "The start roster sender is no longer in the Steam lobby.";
                return false;
            }

            bool ownerIsPlayerOne = false;
            ulong ownerId = (ulong)SteamManager.instance.currentLobby.Owner.Id;
            for (var index = 0; index < roster.Players.Length; index++)
            {
                PlayerDescriptor player = roster.Players[index];
                if (!members.Contains(player.SteamId))
                {
                    reason = "The start roster contains a player outside the current Steam lobby.";
                    return false;
                }

                ownerIsPlayerOne |= player.Slot == 0 && player.SteamId == ownerId;
            }

            if (!ownerIsPlayerOne)
            {
                reason = "The Steam lobby owner must occupy BoplEight player slot one.";
                return false;
            }

            if (FindBySteamId(roster, (ulong)SteamClient.SteamId) == null)
            {
                reason = "The start roster does not include the local Steam player.";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool ValidateRosterAssets(StartRoster roster, out string reason)
        {
            CharacterSelectHandler_online handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
            if (handler != null)
            {
                PaletteRuntime.ExtendPlayerColors(handler.playerColors);
            }

            int colorCount = handler == null || handler.playerColors == null
                ? ProtocolConstants.PlayerColorCount
                : handler.playerColors.Length;
            NamedSpriteList abilityIcons = SteamManager.instance.abilityIcons;
            int abilityIconCount = abilityIcons == null || abilityIcons.sprites == null ? 0 : abilityIcons.sprites.Count;
            for (var playerIndex = 0; playerIndex < roster.Players.Length; playerIndex++)
            {
                PlayerDescriptor player = roster.Players[playerIndex];
                if (player.PlayerColorId >= colorCount)
                {
                    reason = "The start roster selected a player color that is unavailable locally.";
                    return false;
                }

                for (var abilityIndex = 0; abilityIndex < roster.Settings.AbilityCount; abilityIndex++)
                {
                    if (player.AbilityIds[abilityIndex] >= abilityIconCount)
                    {
                        reason = "The start roster selected an ability that is unavailable locally.";
                        return false;
                    }
                }
            }

            reason = null;
            return true;
        }

        private static bool ValidateRosterConnections(StartRoster roster, out string reason)
        {
            if (SteamManager.instance.connectedPlayers.Count != roster.Players.Length - 1)
            {
                reason = "The local Steam connection mesh does not match the prepared roster.";
                return false;
            }

            ulong localSteamId = (ulong)SteamClient.SteamId;
            for (var index = 0; index < roster.Players.Length; index++)
            {
                PlayerDescriptor player = roster.Players[index];
                if (player.SteamId == localSteamId)
                {
                    continue;
                }

                SteamConnection connection = FindConnection(player.SteamId);
                if (connection == null || !connection.Connected)
                {
                    reason = "The local Steam connection mesh is not ready for roster slot " + (player.Slot + 1) + ".";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static byte CheckedPlayerColorIndex(int value, string label)
        {
            if (value < 0 || value >= ProtocolConstants.PlayerColorCount)
            {
                throw new InvalidOperationException("The " + label + " must be between 0 and 11.");
            }

            return (byte)value;
        }

        private static byte CheckedTeamIndex(int value, string label)
        {
            if (value < 0 || value >= ProtocolConstants.MaximumPlayers)
            {
                throw new InvalidOperationException("The " + label + " must be between 0 and 7.");
            }

            return (byte)value;
        }

        private static ushort NextSequence(SteamManager manager)
        {
            if (NextStartGameSequenceField == null)
            {
                throw new MissingFieldException(typeof(SteamManager).FullName, "nextStartGameSeq");
            }

            ushort sequence = (ushort)NextStartGameSequenceField.GetValue(manager);
            NextStartGameSequenceField.SetValue(manager, (ushort)(sequence + 1));
            return sequence;
        }

        private static void MirrorVanillaStartParameters(StartRoster roster)
        {
            StartRequestPacket vanilla = default(StartRequestPacket);
            vanilla.seqNum = roster.Settings.SequenceNumber;
            vanilla.seed = roster.Settings.Seed;
            vanilla.nrOfPlayers = (byte)roster.Players.Length;
            vanilla.nrOfAbilites = roster.Settings.AbilityCount;
            vanilla.currentLevel = roster.Settings.Level;
            vanilla.frameBufferSize = roster.Settings.FrameBufferSize;
            vanilla.isDemoMask = roster.Settings.DemoMask;

            for (var index = 0; index < roster.Players.Length; index++)
            {
                PlayerDescriptor player = roster.Players[index];
                switch (player.Slot)
                {
                    case 0:
                        vanilla.p1_id = player.SteamId;
                        vanilla.p1_color = player.PlayerColorId;
                        vanilla.p1_team = player.TeamId;
                        vanilla.p1_ability1 = player.AbilityIds[0];
                        vanilla.p1_ability2 = player.AbilityIds[1];
                        vanilla.p1_ability3 = player.AbilityIds[2];
                        break;
                    case 1:
                        vanilla.p2_id = player.SteamId;
                        vanilla.p2_color = player.PlayerColorId;
                        vanilla.p2_team = player.TeamId;
                        vanilla.p2_ability1 = player.AbilityIds[0];
                        vanilla.p2_ability2 = player.AbilityIds[1];
                        vanilla.p2_ability3 = player.AbilityIds[2];
                        break;
                    case 2:
                        vanilla.p3_id = player.SteamId;
                        vanilla.p3_color = player.PlayerColorId;
                        vanilla.p3_team = player.TeamId;
                        vanilla.p3_ability1 = player.AbilityIds[0];
                        vanilla.p3_ability2 = player.AbilityIds[1];
                        vanilla.p3_ability3 = player.AbilityIds[2];
                        break;
                    case 3:
                        vanilla.p4_id = player.SteamId;
                        vanilla.p4_color = player.PlayerColorId;
                        vanilla.p4_team = player.TeamId;
                        vanilla.p4_ability1 = player.AbilityIds[0];
                        vanilla.p4_ability2 = player.AbilityIds[1];
                        vanilla.p4_ability3 = player.AbilityIds[2];
                        break;
                }
            }

            SteamManager.startParameters = vanilla;
        }

        private static Player CreatePlayer(PlayerDescriptor descriptor, MatchStartSettings settings, PlayerColors playerColors)
        {
            NamedSpriteList abilityIcons = SteamManager.instance.abilityIcons;
            var player = new Player();
            player.Id = descriptor.Slot + 1;
            player.Color = playerColors.playerColors[descriptor.PlayerColorId].playerMaterial;
            player.Team = descriptor.TeamId;
            player.IsLocalPlayer = false;
            player.UsesKeyboardAndMouse = descriptor.UsesKeyboardAndMouse;
            for (var abilityIndex = 0; abilityIndex < settings.AbilityCount; abilityIndex++)
            {
                byte abilityId = descriptor.AbilityIds[abilityIndex];
                player.Abilities.Add(abilityIcons.sprites[abilityId].associatedGameObject);
                player.AbilityIcons.Add(abilityIcons.sprites[abilityId].sprite);
            }

            player.steamId = descriptor.SteamId;
            return player;
        }

        private static void ChangePlayerAbilities(Player player, PlayerDescriptor descriptor, MatchStartSettings settings)
        {
            NamedSpriteList abilityIcons = SteamManager.instance.abilityIcons;
            player.Abilities.Clear();
            player.AbilityIcons.Clear();
            for (var abilityIndex = 0; abilityIndex < settings.AbilityCount; abilityIndex++)
            {
                byte abilityId = descriptor.AbilityIds[abilityIndex];
                player.Abilities.Add(abilityIcons.sprites[abilityId].associatedGameObject);
                player.AbilityIcons.Add(abilityIcons.sprites[abilityId].sprite);
            }
        }

        [HarmonyPatch(typeof(SteamManager), "HostGame")]
        private static class HostGamePatch
        {
            private static bool Prefix(SteamManager __instance, PlayerInit hostPlayer)
            {
                suppressNextForceStart = false;
                try
                {
                    string compatibilityReason;
                    if (!PeerCompatibility.AllConnectedPeersAreCompatible(out compatibilityReason))
                    {
                        throw new InvalidOperationException(compatibilityReason);
                    }

                    __instance.currentLobby.SetData("LFM", "0");
                    __instance.currentLobby.SetFriendsOnly();
                    __instance.currentLobby.SetJoinable(false);
                    StartRoster roster = BuildInitialRoster(__instance, hostPlayer);
                    string reason;
                    if (!RosterStartCoordinator.BeginAsOwner(roster, out reason))
                    {
                        throw new InvalidOperationException(reason);
                    }

                    BoplEightPlugin.Log.LogInfo("Preparing BoplEight match sequence " + roster.Settings.SequenceNumber + " with " + roster.Players.Length + " players.");
                }
                catch (Exception exception)
                {
                    BoplEightPlugin.Log.LogError("Could not start the BoplEight match: " + exception);
                    RosterStartCoordinator.RestoreInitialStartState();
                    suppressNextForceStart = true;
                    CharacterSelectHandler_online.startButtonAvailable = false;
                    if (SteamManager.instance != null)
                    {
                        __instance.currentLobby.SetJoinable(true);
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterSelectHandler_online), "ForceStartGame")]
        private static class ForceStartGamePatch
        {
            private static bool Prefix(PlayerColors pcs)
            {
                if (RosterStartCoordinator.HasPendingRoster && !RosterStartCoordinator.IsApplyingCommittedRoster)
                {
                    return false;
                }

                StartRoster roster = BoplEightSession.ActiveRoster;
                if (roster == null)
                {
                    if (suppressNextForceStart)
                    {
                        suppressNextForceStart = false;
                        return false;
                    }

                    return true;
                }

                CharacterSelectHandler_online handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
                if (pcs == null && handler != null)
                {
                    pcs = handler.playerColors;
                }

                if (pcs == null)
                {
                    throw new InvalidOperationException("BoplEight could not find the character-selection color palette.");
                }

                PaletteRuntime.ExtendPlayerColors(pcs);
                Updater.ReInit();
                Updater.InitSeed(roster.Settings.Seed);
                GameLobby.nrOfAbilities = roster.Settings.AbilityCount;

                PlayerDescriptor[] descriptors = SortedPlayers(roster);
                var players = new List<Player>(descriptors.Length);
                Player localPlayer = null;
                ulong localSteamId = (ulong)SteamClient.SteamId;
                for (var index = 0; index < descriptors.Length; index++)
                {
                    Player player = CreatePlayer(descriptors[index], roster.Settings, pcs);
                    players.Add(player);
                    if (descriptors[index].SteamId == localSteamId)
                    {
                        localPlayer = player;
                    }
                }

                if (localPlayer == null)
                {
                    throw new InvalidOperationException("BoplEight could not map the local Steam player into the accepted roster.");
                }

                localPlayer.IsLocalPlayer = true;
                localPlayer.inputDevice = CharacterSelectHandler_online.localPlayerInit.inputDevice;
                localPlayer.UsesKeyboardAndMouse = CharacterSelectHandler_online.localPlayerInit.usesKeyboardMouse;
                localPlayer.CustomKeyBinding = CharacterSelectHandler_online.localPlayerInit.keybindOverride;
                CharacterSelectHandler_online.startButtonAvailable = false;
                PlayerHandler.Get().SetPlayerList(players);
                SteamManager.instance.StartHostedGame();
                if (SteamManager.networkClientHandle == null || !SteamManager.networkClientHandle.hasBeenInitialized)
                {
                    throw new InvalidOperationException("BoplEight failed to initialize the complete network client mesh.");
                }

                AudioManager audioManager = AudioManager.Get();
                if (audioManager != null)
                {
                    audioManager.Play("startGame");
                }
                GameSession.Init();
                SceneManager.LoadScene("Level1");
                if (!WinnerTriangleCanvas.HasBeenSpawned)
                {
                    SceneManager.LoadScene("winnerTriangle", LoadSceneMode.Additive);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "InitNetworkClient")]
        private static class InitNetworkClientPatch
        {
            private static bool Prefix(SteamManager __instance)
            {
                StartRoster roster = BoplEightSession.ActiveRoster;
                if (roster == null)
                {
                    return true;
                }

                PlayerDescriptor local = FindBySteamId(roster, (ulong)SteamClient.SteamId);
                if (local == null)
                {
                    throw new InvalidOperationException("BoplEight could not initialize networking without a local roster slot.");
                }

                var clients = new List<Client>(roster.Players.Length - 1);
                PlayerDescriptor[] players = SortedPlayers(roster);
                for (var index = 0; index < players.Length; index++)
                {
                    if (players[index].SteamId == local.SteamId)
                    {
                        continue;
                    }

                    SteamConnection connection = FindConnection(players[index].SteamId);
                    if (connection != null && connection.Connected)
                    {
                        clients.Add(new Client(players[index].Slot + 1, connection));
                    }
                }

                if (clients.Count != roster.Players.Length - 1)
                {
                    throw new InvalidOperationException("BoplEight could not map every remote roster slot to a connected Steam peer.");
                }

                __instance.networkClient.Init(
                    clients,
                    local.Slot + 1,
                    roster.Settings.Level,
                    roster.Settings.FrameBufferSize,
                    __instance.checkForDesyncs);
                SteamManager.networkClientHandle = __instance.networkClient;
                return false;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "HostNextLevel")]
        private static class HostNextLevelPatch
        {
            private static bool Prefix(SteamManager __instance)
            {
                StartRoster current = BoplEightSession.ActiveRoster;
                if (current == null)
                {
                    return true;
                }

                if (RosterStartCoordinator.HasPendingRoster)
                {
                    return false;
                }

                abortNextLevelLoad = false;
                try
                {
                    StartRoster roster = BuildNextLevelRoster(__instance, current);
                    string reason;
                    if (!RosterStartCoordinator.BeginAsOwner(roster, out reason))
                    {
                        throw new InvalidOperationException(reason);
                    }

                    BoplEightPlugin.Log.LogInfo("Preparing BoplEight next-level sequence " + roster.Settings.SequenceNumber + " with " + roster.Players.Length + " players.");
                }
                catch (Exception exception)
                {
                    BoplEightPlugin.Log.LogError("Could not advance the BoplEight match: " + exception);
                    abortNextLevelLoad = true;
                    BoplEightSession.RequestMatchTermination("Could not advance the BoplEight match: " + exception.Message);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "ForceLoadNextLevel")]
        private static class ForceLoadNextLevelPatch
        {
            private static bool Prefix()
            {
                if (RosterStartCoordinator.HasPendingRoster && !RosterStartCoordinator.IsApplyingCommittedRoster)
                {
                    return false;
                }

                if (abortNextLevelLoad)
                {
                    abortNextLevelLoad = false;
                    return false;
                }

                StartRoster roster = BoplEightSession.ActiveRoster;
                if (roster == null)
                {
                    return true;
                }

                if (SteamManager.networkClientHandle == null)
                {
                    throw new InvalidOperationException("BoplEight could not load the next level because the network host was unavailable.");
                }

                SteamManager.networkClientHandle.DeInit();
                List<Player> players = PlayerHandler.Get().PlayerList();
                for (var index = players.Count - 1; index >= 0; index--)
                {
                    PlayerDescriptor descriptor = FindByPlayerId(roster, players[index].Id);
                    SteamConnection connection = descriptor == null ? null : FindConnection(descriptor.SteamId);
                    if (descriptor == null || (!players[index].IsLocalPlayer && (connection == null || !connection.Connected)))
                    {
                        players.RemoveAt(index);
                    }
                }

                if (players.Count < ProtocolConstants.MinimumPlayers)
                {
                    throw new InvalidOperationException("BoplEight could not continue with fewer than two connected players.");
                }

                for (var index = 0; index < players.Count; index++)
                {
                    PlayerDescriptor descriptor = FindByPlayerId(roster, players[index].Id);
                    if (descriptor != null)
                    {
                        ChangePlayerAbilities(players[index], descriptor, roster.Settings);
                    }
                }

                GameSession.SetCurrentLevel(roster.Settings.Level);
                CharacterStatsList.ForceNextLevelImmediately();

                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterStatsList), "OnNextLevelWasSuggested")]
        private static class OnNextLevelWasSuggestedPatch
        {
            private static bool Prefix(int hostPlayerId, NamedSpriteList AbilityIcons)
            {
                if (BoplEightSession.ActiveRoster == null || GameSessionHandler.GameIsPaused)
                {
                    return true;
                }

                if (!RosterStartCoordinator.HasPendingRoster && SteamManager.instance != null)
                {
                    SteamManager.instance.HostNextLevel(PlayerHandler.Get().GetPlayer(hostPlayerId), AbilityIcons);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterStatsList), "ForceNextLevelImmediately")]
        private static class ForceNextLevelImmediatelyPatch
        {
            private static bool Prefix()
            {
                return BoplEightSession.ActiveRoster == null
                    || ((!RosterStartCoordinator.HasPendingRoster || RosterStartCoordinator.IsApplyingCommittedRoster)
                        && !abortNextLevelLoad);
            }
        }
    }
}
