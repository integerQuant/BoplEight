# Architecture

## Compatibility Boundary

`BoplEightPlugin` hashes `Assembly-CSharp.dll` before installing Harmony patches. Unknown game builds stay unpatched. Lobby metadata also binds the plugin version, protocol version, game assembly hash, and player limits.

## Protocol

Protocol v3 uses reserved packet types that avoid all vanilla packet lengths. `PacketRouter` claims malformed reserved packets so they cannot fall through into vanilla dispatch. Steam senders are checked against current lobby membership and live connections before custom packet handling.

Peer handshakes compare deterministic ability manifests. Match rosters use prepare, roster acknowledgement, commit, and commit acknowledgement controls bound to a SHA-256-derived identity of the exact serialized roster. Rejections and commits are retried, and disconnects invalidate pending transactions.

## Runtime

- `RosterRuntime` builds, validates, activates, and applies two-through-eight-player rosters.
- `RosterStartCoordinator` implements transactional initial and next-level starts.
- `FrameRuntime` aggregates vanilla per-player input packets into dynamic roster frames and applies them before deterministic simulation ticks.
- `LobbyUiRuntime` expands character selection and remote Steam overlays.
- `PaletteRuntime`, `SpawnRuntime`, and `GameplayRuntime` expand colors, teams, spawns, UI, achievements, and fixed-size physics buffers.
- `PeerCompatibility` blocks starts until every connected peer proves matching code and deterministic assets.

Replay recording is disabled for active BoplEight matches because the shipped replay format stores only four input slots.

## Failure Policy

BoplEight prefers fail-stop behavior over divergent simulation. Authority changes, roster disconnects, incomplete frames, failed transactions, malformed sender mappings, or desyncs request deferred teardown after Harmony call stacks unwind.

## Installer

The installer is a .NET Framework Windows Forms executable with an embedded ZIP payload. `InstallerCore` performs Steam discovery, exact game validation, safe extraction, existing-BepInEx preservation, repair, and BoplEight-only uninstall. The release remains unsigned unless a maintainer signs it externally.
