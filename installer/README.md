# BoplEight Installer

Build the self-contained Windows installer from the repository root with:

```powershell
./installer/build-installer.ps1 -GameRoot "D:\SteamLibrary\steamapps\common\Bopl Battle"
```

The build runs the BoplEight protocol tests and installer tests, stages the local official BepInEx 5.4.23.2 files, embeds the payload, and produces:

- `dist/BoplEight-Setup-1.0.5.exe`
- `dist/BoplEight-Setup-1.0.5.sha256.txt`

## Friend Instructions

1. Download `BoplEight-Setup-1.0.5.exe`.
2. If Windows SmartScreen appears, click **More info**, then **Run anyway**.
3. Approve the Windows administrator prompt.
4. Click **Install BoplEight**.
5. Launch Bopl Battle normally through Steam.

Every player must install the same BoplEight version. Running the installer again offers repair/update and uninstall actions. Uninstall removes BoplEight but intentionally keeps shared BepInEx files.

For standalone-clone prerequisites, separate BepInEx pack usage, payload tests, and release steps, see `../docs/BUILDING.md` and `../docs/RELEASING.md`.
