using System;
using BoplEight.Match;
using BoplEight.Protocol;

namespace BoplEight.Tests
{
    internal static class ProtocolTests
    {
        private static int Main()
        {
            return TestRunner.Run(
                StartRosterRoundTripsEightPlayers,
                StartRosterCarriesDeterministicMatchSettings,
                StartRosterPreservesSelectedColorsAndTeams,
                StartRosterRejectsUnavailablePlayerColors,
                StartRosterPreservesInputDeviceMetadata,
                StartRosterRejectsInvalidPlayerCounts,
                StartRosterRejectsDuplicateSlotsAndSteamIds,
                PacketsRejectUnknownProtocolVersions,
                PeerHandshakePacketsRouteWithoutVanillaLengthCollisions,
                PeerHandshakePacketsRejectWrongCompatibilityTokens,
                RosterControlPacketsRoundTripSequenceNumbers,
                RosterControlsBindToExactRosterContents,
                DifferentRostersCannotShareAControlIdentity,
                StartRosterPacketsAvoidEveryVanillaPacketLength,
                CustomStartPacketsAreRecognizedBeforeVanillaDispatch,
                VanillaPacketsAreNotClaimedByMagicBytesAlone,
                InvalidCustomStartPacketsAreConsumedInsteadOfFallingIntoVanillaDispatch,
                ReservedCustomPacketsNeverFallThroughToVanillaDispatch,
                VanillaLobbyPacketsAcceptEverySupportedPlayerColor,
                VanillaLobbyPacketsRejectOutOfRangeSelectionFields,
                RuntimeModelTests.RosterStateAcceptsStrictlyNewerHostRosters,
                RuntimeModelTests.RosterStateAcceptsSequenceWraparound,
                RuntimeModelTests.RosterStateRejectsStaleAndForeignRosters,
                RuntimeModelTests.RosterFramesSupportSparseEightPlayerSlots,
                RuntimeModelTests.RosterLayoutPlacesEightPlayersInUniqueColumns,
                RuntimeModelTests.RosterLayoutKeepsPlayerFiveSeparateFromPlayerOne,
                RuntimeModelTests.RosterLayoutFitsEightAbilitySelectorsAcrossVanillaWidth,
                RuntimeModelTests.RosterLayoutPreservesPrefabScaleWhenFittingEightColumns,
                RuntimeModelTests.RosterLayoutKeepsOffsetReadyCardOnVisualBaseline,
                RuntimeModelTests.RosterLayoutExpandsAnimationTravelForScaledCards,
                RuntimeModelTests.RosterLayoutStartsScaledControlsAtTheFittedBoundary,
                RuntimeModelTests.RosterLayoutKeepsScorePortraitsCompact,
                RuntimeModelTests.RosterLayoutPreservesRoundSummaryTiming,
                RuntimeModelTests.SparseAvatarReadinessPreservesConnectionSlots,
                RuntimeModelTests.RosterLayoutIdentifiesOnlyExpandedRemoteSlots,
                PaletteTests.ExtendedTeamPaletteProvidesFourDistinctTeamChoices,
                MatchCompatibilityTests.LobbiesAcceptTwoThroughEightPlayers,
                MatchCompatibilityTests.LobbiesAllowAHostToFormAMatchBeforeASecondPlayerJoins,
                MatchCompatibilityTests.LobbyInvitesAwaitCompatibilityMetadataBeforeRejecting,
                MatchCompatibilityTests.RefreshedVanillaLobbyInvitesAreRejected,
                MatchCompatibilityTests.CachedCompatibleLobbyInvitesJoinWithoutRefreshing,
                MatchCompatibilityTests.NewLobbyJoinRequestsReplaceStalePendingRequests,
                MatchCompatibilityTests.LobbyJoinTimeoutsAreConsumedOnce,
                MatchCompatibilityTests.PendingLobbyJoinCancellationIsConsumedOnce,
                MatchCompatibilityTests.SteamLobbyJoinGateAllowsOnlyOneActiveJoin,
                MatchCompatibilityTests.LobbiesRejectMismatchedVersions,
                MatchCompatibilityTests.LobbiesRejectMismatchedGameAssemblies);
        }

        private static void StartRosterRoundTripsEightPlayers()
        {
            var original = new StartRoster(
                NewMatchSettings(),
                new PlayerDescriptor[]
                {
                    NewPlayer(0, 101, 0, 0, false, 1, 2, 3),
                    NewPlayer(1, 102, 1, 1, true, 4, 5, 6),
                    NewPlayer(2, 103, 2, 2, false, 7, 8, 9),
                    NewPlayer(3, 104, 3, 3, true, 10, 11, 12),
                    NewPlayer(4, 105, 4, 4, false, 13, 14, 15),
                    NewPlayer(5, 106, 5, 5, true, 16, 17, 18),
                    NewPlayer(6, 107, 6, 6, false, 19, 20, 21),
                    NewPlayer(7, 108, 7, 7, true, 22, 23, 24)
                });

            StartRoster decoded = PacketCodec.DecodeStartRoster(PacketCodec.EncodeStartRoster(original));

            Assert.Equal(8, decoded.Players.Length, "Eight players should survive start-roster serialization.");
            Assert.Equal((ulong)108, decoded.Players[7].SteamId, "The final player Steam ID should round-trip.");
            Assert.Equal((byte)24, decoded.Players[7].AbilityIds[2], "The final player ability selection should round-trip.");
        }

        private static void StartRosterCarriesDeterministicMatchSettings()
        {
            var original = new StartRoster(
                new MatchStartSettings(99, 123456, 3, 17, 4, 7),
                new PlayerDescriptor[]
                {
                    NewPlayer(0, 111, 0, 0, false, 1, 2, 3),
                    NewPlayer(1, 112, 1, 1, true, 4, 5, 6)
                });

            StartRoster decoded = PacketCodec.DecodeStartRoster(PacketCodec.EncodeStartRoster(original));

            Assert.Equal((ushort)99, decoded.Settings.SequenceNumber, "The start sequence should round-trip.");
            Assert.Equal((uint)123456, decoded.Settings.Seed, "The deterministic random seed should round-trip.");
            Assert.Equal((byte)3, decoded.Settings.AbilityCount, "The selected ability count should round-trip.");
            Assert.Equal((byte)17, decoded.Settings.Level, "The level should round-trip.");
            Assert.Equal((byte)4, decoded.Settings.FrameBufferSize, "The frame buffer should round-trip.");
            Assert.Equal((byte)7, decoded.Settings.DemoMask, "The demo entitlement mask should round-trip.");
        }

        private static void StartRosterPreservesSelectedColorsAndTeams()
        {
            var original = new StartRoster(
                NewMatchSettings(),
                new PlayerDescriptor[]
                {
                    NewPlayer(0, 201, 11, 3, false, 1, 1, 1),
                    NewPlayer(1, 202, 8, 1, false, 2, 2, 2),
                    NewPlayer(2, 203, 5, 3, false, 3, 3, 3),
                    NewPlayer(3, 204, 4, 0, false, 4, 4, 4)
                });

            StartRoster decoded = PacketCodec.DecodeStartRoster(PacketCodec.EncodeStartRoster(original));

            Assert.Equal((byte)11, decoded.Players[0].PlayerColorId, "Every supported cosmetic player color must round-trip.");
            Assert.Equal((byte)8, decoded.Players[1].PlayerColorId, "Cosmetic player colors must round-trip independently of roster capacity.");
            Assert.Equal((byte)3, decoded.Players[0].TeamId, "Player-selected teams must not be reassigned.");
            Assert.Equal((byte)3, decoded.Players[2].TeamId, "Multiple players may select the same team.");
        }

        private static void StartRosterRejectsUnavailablePlayerColors()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                NewPlayer(0, 205, ProtocolConstants.PlayerColorCount, 0, false, 1, 2, 3);
            }, "Player color IDs beyond the supported palette must be rejected.");
        }

        private static void StartRosterPreservesInputDeviceMetadata()
        {
            var original = new StartRoster(
                NewMatchSettings(),
                new PlayerDescriptor[]
                {
                    NewPlayer(0, 211, 0, 0, false, 1, 2, 3),
                    NewPlayer(7, 212, 7, 7, true, 4, 5, 6)
                });

            StartRoster decoded = PacketCodec.DecodeStartRoster(PacketCodec.EncodeStartRoster(original));

            Assert.False(decoded.Players[0].UsesKeyboardAndMouse, "Gamepad metadata should round-trip.");
            Assert.True(decoded.Players[1].UsesKeyboardAndMouse, "Keyboard and mouse metadata should round-trip.");
        }

        private static void StartRosterRejectsInvalidPlayerCounts()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
                {
                    NewPlayer(0, 301, 0, 0, false, 1, 2, 3)
                });
            }, "A modded match requires at least two players.");

            Assert.Throws<ArgumentException>(delegate
            {
                var players = new PlayerDescriptor[9];
                for (byte i = 0; i < players.Length; i++)
                {
                    players[i] = NewPlayer(i, (ulong)(400 + i), 0, 0, false, 1, 2, 3);
                }

                new StartRoster(NewMatchSettings(), players);
            }, "A modded match must not accept more than eight players.");
        }

        private static void StartRosterRejectsDuplicateSlotsAndSteamIds()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
                {
                    NewPlayer(0, 501, 0, 0, false, 1, 2, 3),
                    NewPlayer(0, 502, 1, 1, false, 4, 5, 6)
                });
            }, "A roster must have unique simulation slots.");

            Assert.Throws<ArgumentException>(delegate
            {
                new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
                {
                    NewPlayer(0, 601, 0, 0, false, 1, 2, 3),
                    NewPlayer(1, 601, 1, 1, false, 4, 5, 6)
                });
            }, "A roster must have unique Steam IDs.");
        }

        private static void PacketsRejectUnknownProtocolVersions()
        {
            byte[] packet = PacketCodec.EncodeStartRoster(new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
            {
                NewPlayer(0, 701, 0, 0, false, 1, 2, 3),
                NewPlayer(1, 702, 1, 1, true, 4, 5, 6)
            }));
            packet[3] = ProtocolConstants.Version + 1;

            Assert.Throws<ProtocolException>(delegate
            {
                PacketCodec.DecodeStartRoster(packet);
            }, "Unknown protocol versions must be rejected before simulation starts.");
        }

        private static void PeerHandshakePacketsRouteWithoutVanillaLengthCollisions()
        {
            const ulong manifest = 0x1020304050607080UL;
            byte[] hello = PacketCodec.EncodePeerHandshake(ModMessageType.Hello, manifest);
            byte[] acknowledgement = PacketCodec.EncodePeerHandshake(ModMessageType.HelloAck, manifest);

            PacketRoute helloRoute = PacketRouter.Route(hello);
            Assert.Equal(20, hello.Length, "Peer handshakes must avoid every vanilla packet length.");
            Assert.Equal(PacketRouteKind.Hello, helloRoute.Kind, "A valid peer hello should route to the handshake handler.");
            Assert.Equal(manifest, helloRoute.ManifestToken, "Peer handshakes must carry the deterministic asset manifest.");
            Assert.Equal(PacketRouteKind.HelloAck, PacketRouter.Route(acknowledgement).Kind, "A valid peer acknowledgement should route to the handshake handler.");
        }

        private static void PeerHandshakePacketsRejectWrongCompatibilityTokens()
        {
            byte[] hello = PacketCodec.EncodePeerHandshake(ModMessageType.Hello, 1);
            hello[4] ^= 0xff;

            PacketRoute route = PacketRouter.Route(hello);

            Assert.Equal(PacketRouteKind.RejectedBoplEightPacket, route.Kind, "A peer using a different compatibility token must be rejected.");
            Assert.Contains("compatibility", route.Reason, "The handshake rejection should explain the compatibility mismatch.");
        }

        private static void RosterControlPacketsRoundTripSequenceNumbers()
        {
            const ulong rosterToken = 0x1020304050607080UL;
            PacketRoute acknowledgement = PacketRouter.Route(PacketCodec.EncodeRosterControl(ModMessageType.RosterAck, 1234, rosterToken));
            PacketRoute commit = PacketRouter.Route(PacketCodec.EncodeRosterControl(ModMessageType.CommitRoster, 1234, rosterToken));
            PacketRoute commitAcknowledgement = PacketRouter.Route(PacketCodec.EncodeRosterControl(ModMessageType.CommitAck, 1234, rosterToken));
            PacketRoute rejection = PacketRouter.Route(PacketCodec.EncodeRosterControl(ModMessageType.RejectRoster, 1234, rosterToken));

            Assert.Equal(PacketRouteKind.RosterAck, acknowledgement.Kind, "Roster acknowledgements should have a dedicated route.");
            Assert.Equal(PacketRouteKind.CommitRoster, commit.Kind, "Roster commits should have a dedicated route.");
            Assert.Equal(PacketRouteKind.CommitAck, commitAcknowledgement.Kind, "Commit acknowledgements should have a dedicated route.");
            Assert.Equal(PacketRouteKind.RejectRoster, rejection.Kind, "Roster rejections should have a dedicated route.");
            Assert.Equal((ushort)1234, acknowledgement.SequenceNumber, "Roster control sequence numbers must round-trip.");
            Assert.Equal(rosterToken, acknowledgement.RosterToken, "Roster control identities must round-trip.");
            Assert.Equal(24, PacketCodec.EncodeRosterControl(ModMessageType.CommitRoster, 1, rosterToken).Length, "Roster controls must avoid vanilla packet lengths.");
        }

        private static void RosterControlsBindToExactRosterContents()
        {
            StartRoster roster = new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
            {
                NewPlayer(0, 1201, 0, 0, false, 1, 2, 3),
                NewPlayer(1, 1202, 1, 1, true, 4, 5, 6)
            });
            ulong rosterToken = PacketCodec.ComputeRosterToken(roster);

            PacketRoute route = PacketRouter.Route(PacketCodec.EncodeRosterControl(
                ModMessageType.CommitRoster,
                roster.Settings.SequenceNumber,
                rosterToken));

            Assert.Equal(rosterToken, route.RosterToken, "A commit must identify the exact prepared roster.");
        }

        private static void DifferentRostersCannotShareAControlIdentity()
        {
            StartRoster first = new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
            {
                NewPlayer(0, 1301, 0, 0, false, 1, 2, 3),
                NewPlayer(1, 1302, 1, 1, true, 4, 5, 6)
            });
            StartRoster second = new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
            {
                NewPlayer(0, 1301, 0, 0, false, 1, 2, 3),
                NewPlayer(1, 1302, 1, 2, true, 4, 5, 6)
            });

            Assert.False(
                PacketCodec.ComputeRosterToken(first) == PacketCodec.ComputeRosterToken(second),
                "Different roster contents must not accept the same acknowledgement or commit.");
        }

        private static void StartRosterPacketsAvoidEveryVanillaPacketLength()
        {
            for (byte playerCount = ProtocolConstants.MinimumPlayers; playerCount <= ProtocolConstants.MaximumPlayers; playerCount++)
            {
                var players = new PlayerDescriptor[playerCount];
                for (byte slot = 0; slot < playerCount; slot++)
                {
                    players[slot] = NewPlayer(slot, (ulong)(1100 + slot), slot, slot, false, 1, 2, 3);
                }

                int length = PacketCodec.EncodeStartRoster(new StartRoster(NewMatchSettings(), players)).Length;
                bool collides = length == 1 || length == 2 || length == 3 || length == 4 || length == 6
                    || length == 14 || length == 15 || length == 67 || length == 83 || length == 105;
                Assert.False(collides, "A " + playerCount + "-player roster must not collide with vanilla packet length " + length + ".");
            }
        }

        private static void CustomStartPacketsAreRecognizedBeforeVanillaDispatch()
        {
            byte[] packet = PacketCodec.EncodeStartRoster(new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
            {
                NewPlayer(0, 801, 0, 0, false, 1, 2, 3),
                NewPlayer(1, 802, 1, 1, true, 4, 5, 6),
                NewPlayer(2, 803, 2, 2, false, 7, 8, 9),
                NewPlayer(3, 804, 3, 3, true, 10, 11, 12)
            }));

            PacketRoute route = PacketRouter.Route(packet);

            Assert.Equal(PacketRouteKind.StartRoster, route.Kind, "The custom router must claim a valid BoplEight start packet before vanilla dispatch.");
            Assert.Equal(4, route.StartRoster.Players.Length, "The routed start packet should retain its roster.");
        }

        private static void VanillaPacketsAreNotClaimedByMagicBytesAlone()
        {
            var packet = new byte[67];
            packet[0] = 1;
            packet[1] = (byte)(ProtocolConstants.Magic & 0xff);
            packet[2] = (byte)(ProtocolConstants.Magic >> 8);

            PacketRoute route = PacketRouter.Route(packet);

            Assert.Equal(PacketRouteKind.NotBoplEightPacket, route.Kind, "A vanilla packet type must not be claimed solely because payload bytes resemble the custom magic.");
        }

        private static void InvalidCustomStartPacketsAreConsumedInsteadOfFallingIntoVanillaDispatch()
        {
            byte[] packet = PacketCodec.EncodeStartRoster(new StartRoster(NewMatchSettings(), new PlayerDescriptor[]
            {
                NewPlayer(0, 901, 0, 0, false, 1, 2, 3),
                NewPlayer(1, 902, 1, 1, true, 4, 5, 6),
                NewPlayer(2, 903, 2, 2, false, 7, 8, 9),
                NewPlayer(3, 904, 3, 3, true, 10, 11, 12)
            }));
            packet[3] = ProtocolConstants.Version + 1;

            PacketRoute route = PacketRouter.Route(packet);

            Assert.Equal(PacketRouteKind.RejectedBoplEightPacket, route.Kind, "Invalid BoplEight packets must never reach a same-length vanilla handler.");
            Assert.Contains("unsupported", route.Reason, "The rejection should explain why the custom packet was rejected.");
        }

        private static void ReservedCustomPacketsNeverFallThroughToVanillaDispatch()
        {
            PacketRoute truncated = PacketRouter.Route(new byte[] { (byte)ModMessageType.CommitRoster });
            PacketRoute unsupported = PacketRouter.Route(new byte[]
            {
                115,
                (byte)(ProtocolConstants.Magic & 0xff),
                (byte)(ProtocolConstants.Magic >> 8),
                ProtocolConstants.Version
            });

            Assert.Equal(PacketRouteKind.RejectedBoplEightPacket, truncated.Kind, "A truncated reserved control must be consumed.");
            Assert.Equal(PacketRouteKind.RejectedBoplEightPacket, unsupported.Kind, "A reserved custom packet type must be consumed.");
        }

        private static void VanillaLobbyPacketsAcceptEverySupportedPlayerColor()
        {
            Assert.True(
                VanillaLobbyPacketValidator.IsValid(new byte[] { 11, 7, 1 }, 31),
                "Ready-state updates must accept the game's twelfth player color.");
            Assert.True(
                VanillaLobbyPacketValidator.IsValid(new byte[]
                {
                    11, 7, 30, 29, 28, 1, 1,
                    0, 0, 0, 0, 0, 0, 0, 1
                }, 31),
                "Full ready packets must accept supported colors independently of the eight-player limit.");
        }

        private static void VanillaLobbyPacketsRejectOutOfRangeSelectionFields()
        {
            Assert.False(
                VanillaLobbyPacketValidator.IsValid(new byte[] { ProtocolConstants.PlayerColorCount, 0, 1 }, 31),
                "Lobby updates must reject colors outside the local palette.");
            Assert.False(
                VanillaLobbyPacketValidator.IsValid(new byte[] { 0, ProtocolConstants.MaximumPlayers, 1 }, 31),
                "Lobby updates must reject teams outside the eight-team palette.");
            Assert.False(
                VanillaLobbyPacketValidator.IsValid(new byte[] { 0, 0, 2 }, 31),
                "Lobby updates must reject non-boolean ready flags.");
            Assert.False(
                VanillaLobbyPacketValidator.IsValid(new byte[] { 0, 0, 31, 1 }, 31),
                "Ability updates must reject unavailable abilities.");
        }

        private static MatchStartSettings NewMatchSettings()
        {
            return new MatchStartSettings(1, 2, 3, 4, 5, 6);
        }

        internal static PlayerDescriptor NewPlayer(
            byte slot,
            ulong steamId,
            byte playerColorId,
            byte teamId,
            bool usesKeyboardAndMouse,
            byte ability1,
            byte ability2,
            byte ability3)
        {
            return new PlayerDescriptor(
                slot,
                steamId,
                playerColorId,
                teamId,
                usesKeyboardAndMouse,
                new byte[] { ability1, ability2, ability3 });
        }
    }

    internal static class TestRunner
    {
        public static int Run(params Action[] tests)
        {
            var failures = 0;

            foreach (Action test in tests)
            {
                try
                {
                    test();
                    Console.WriteLine("PASS " + test.Method.Name);
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + exception.Message);
                }
            }

            Console.WriteLine("Tests: " + tests.Length + ", Failures: " + failures);
            return failures == 0 ? 0 : 1;
        }
    }

    internal static class Assert
    {
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " Expected: " + expected + ", Actual: " + actual);
            }
        }

        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void False(bool condition, string message)
        {
            if (condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void Contains(string expectedSubstring, string actual, string message)
        {
            if (actual == null || actual.IndexOf(expectedSubstring, StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(message + " Actual: " + actual);
            }
        }
    }
}
