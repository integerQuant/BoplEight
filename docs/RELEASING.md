# Release Procedure

## Version Updates

Keep these values synchronized for a release:

- `BoplEightPlugin.PluginVersion` in `src/Runtime/BoplEightPlugin.cs`.
- `InstallerCore.InstallerVersion` in `installer/src/InstallerCore.cs`.
- Assembly versions in `installer/src/Program.cs`.
- `$installerVersion` in `installer/build-installer.ps1`.
- Package names and instructions in the README files.
- `CHANGELOG.md`.

Increment `ProtocolConstants.Version` only when wire compatibility changes. Older protocol peers are intentionally rejected.

## Build And Verify

1. Run `./build.ps1 -NoInstall` and confirm every test passes.
2. Run `./installer/build-installer.ps1` and confirm installer and packaged-payload tests pass.
3. Run the generated installer against a clean supported Bopl Battle installation.
4. Launch through Steam and inspect both `BepInEx/LogOutput.log` and Unity's `Player.log`.
5. Confirm lobby capacity, team colors, and eight visible character-selection slots.
6. Test real Steam matches with the supported player counts targeted by the release.
7. Scan the release EXE with current antivirus software.
8. Publish the EXE and generated SHA-256 file as release artifacts, not as tracked Git files.

## Repository Hygiene

Before release, inspect `git status` and the staged file list. No game assemblies, game executables, Steam libraries, BepInEx binaries, generated DLLs, installer EXEs, ZIP payloads, logs, or local configuration files belong in the repository.
