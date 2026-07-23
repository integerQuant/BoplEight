# Installing BoplEight

## Recommended Friend Install

Distribute `BoplEight-Setup-1.0.2.exe` from the release artifacts. Each player should:

1. Close Bopl Battle.
2. Run `BoplEight-Setup-1.0.2.exe`.
3. If Windows SmartScreen appears, select **More info**, then **Run anyway**.
4. Approve the administrator prompt.
5. Confirm the automatically detected Bopl Battle folder or use **Browse**.
6. Click **Install BoplEight**.
7. Launch Bopl Battle normally through Steam.

Every player in a lobby needs the same BoplEight version, protocol, supported Bopl Battle build, and deterministic ability manifest.

## Installer Behavior

The installer:

- Detects Bopl Battle across Steam library folders.
- Verifies the exact supported `Assembly-CSharp.dll` SHA-256 before writing files.
- Installs BepInEx 5.4.23.2 when it is absent or incomplete.
- Preserves a complete existing BepInEx installation and unrelated plugins.
- Installs or updates only `BepInEx/plugins/BoplEight.dll` for an existing BepInEx setup.
- Rejects ZIP path traversal and redirected child paths while running elevated.
- Provides repair/update and BoplEight-only uninstall actions.

Uninstall intentionally leaves BepInEx installed because other mods may depend on it.

## Troubleshooting

### Unsupported game version

In Steam, open Bopl Battle's properties, select **Installed Files**, and run **Verify integrity of game files**. If the assembly hash still differs, a new BoplEight build is required.

### Game folder not detected

In Steam, right-click Bopl Battle, select **Manage**, then **Browse local files**. Use the installer's **Browse** button and select the folder containing `BoplBattle.exe`.

### Mod does not load

Check `BepInEx/LogOutput.log` under the game folder. A successful startup includes `Verified all critical roster, frame, simulation, spawn, and packet patches.`

### Friends cannot join

Confirm every player installed the same installer release and has no pending Bopl Battle update. Vanilla clients and mismatched BoplEight clients are intentionally rejected.
