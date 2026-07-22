# BoplEight

A BepInEx 5 plugin implementing an experimental synchronized 2-8 player mode for Bopl Battle.

> BoplEight is build-locked and still requires real multi-client validation. It refuses to patch unsupported Bopl Battle versions.

## Current State

- The plugin targets BepInEx 5.4.23.2 and BoplEight 1.0.1 protocol version 3.
- Runtime patches are applied only to the inspected `Assembly-CSharp.dll` SHA-256 `06A154AF64AD962E534587058219FB94216C5CE53605BB9AF5F77CB433A4AE07`.
- Steam lobbies support two through eight players and reject vanilla, mismatched-plugin, mismatched-protocol, and mismatched-game clients.
- Every peer verifies the same deterministic ability manifest before the host can start.
- Match start and next-level rosters use prepare, acknowledgement, commit, and commit-acknowledgement phases bound to a hash of the exact roster.
- Character selection, player/team colors, spawning, winner UI, ability UI, physics hit buffers, frame aggregation, and simulation input support up to eight roster slots.
- Gameplay retains the game's per-player Steam input transport. BoplEight only replaces fixed four-player aggregation and application.
- Replay recording is disabled for BoplEight matches because the game's replay format contains four input slots.
- A roster disconnect, authority change, incomplete frame, failed commit, or desync ends the match instead of continuing with divergent state.

## Compatibility

Every player must install the same BoplEight build and use the supported game assembly. Mixed deterministic asset manifests, including incompatible full/demo ability sets, are rejected before match start.

BoplEight lobbies are intentionally isolated from unmodded matchmaking. There is no runtime configuration switch for partially enabling the mod.

## Build

Run `./build.ps1` from this directory. The script runs 36 protocol/model/layout tests, compiles `dist/BoplEight.dll`, and installs it as `BepInEx/plugins/BoplEight.dll` unless `-NoInstall` is supplied.

The repository can be cloned anywhere. Pass `-GameRoot`, set `BOPL_GAME_ROOT`, or let the scripts discover Bopl Battle through Steam. See [Building From Source](docs/BUILDING.md).

## Friend Installer

Build the self-contained Windows installer with `./installer/build-installer.ps1`. The release executable is written to `installer/dist/BoplEight-Setup-1.0.1.exe` and includes BepInEx 5.4.23.2 plus BoplEight.

Friends only need to run the EXE, approve the administrator prompt, click **Install BoplEight**, and then launch Bopl Battle normally through Steam. No mod manager or manual file placement is required. Because the installer is not code-signed, Windows SmartScreen may require **More info** followed by **Run anyway**.

See [Installing BoplEight](docs/INSTALLING.md) for troubleshooting and uninstall behavior.

## Repository

- [`src/`](src): protocol, lobby, roster, UI, and runtime implementation.
- [`tests/`](tests): standalone deterministic protocol and model tests.
- [`installer/`](installer): Windows installer source, tests, payload notices, and build procedure.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): protocol and runtime design.
- [`docs/RELEASING.md`](docs/RELEASING.md): release checklist and artifact policy.
- [`CONTRIBUTING.md`](CONTRIBUTING.md): development requirements and safety rules.

## Validation Status

- Automated protocol and model tests pass.
- A clean game startup verifies every critical Harmony patch and loads without BepInEx errors.
- Real 2-8 client Steam matches, disconnect recovery, paused next-level transitions, and every level's synthesized extra spawn anchors still require live multiplayer testing.
