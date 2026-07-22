# Contributing

## Requirements

- Windows with Windows PowerShell 5.1 or newer.
- Bopl Battle installed through Steam.
- The exact supported game assembly documented in `README.md`.
- BepInEx 5.4.23.2 extracted either into the game folder or another local directory.
- The .NET Framework 4.x C# compiler included with Windows.

See `docs/BUILDING.md` for setup and build commands.

## Development Rules

- Do not commit Bopl Battle assemblies, executables, assets, Steam libraries, or any other game files.
- Do not commit BepInEx binaries. The release installer stages them from a local official BepInEx pack.
- Keep packet changes versioned and add protocol tests before changing runtime dispatch.
- Preserve sender binding, lobby membership validation, and exact roster identity on every custom control packet.
- Run `./build.ps1 -NoInstall` and `./installer/build-installer.ps1` before preparing a release.
- Real multiplayer behavior must be validated separately; single-process automated tests cannot prove deterministic Steam synchronization.

## Change Scope

Keep changes minimal and tied to the currently supported game build. BoplEight intentionally refuses to patch unknown `Assembly-CSharp.dll` versions rather than guessing at compatibility.
