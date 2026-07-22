# Building From Source

The repository does not contain proprietary Bopl Battle files or third-party BepInEx binaries. Builds reference a local game installation and an official local BepInEx 5.4.23.2 pack.

## Prerequisites

1. Install Bopl Battle through Steam.
2. Download BepInEx 5.4.23.2 for Windows x64 from the official BepInEx GitHub release.
3. Extract BepInEx into the game folder, or keep the extracted pack in a separate development directory.
4. Use Windows PowerShell 5.1 or newer.

The scripts use the .NET Framework compiler normally located at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

## Build The Plugin

When BepInEx is installed in the game folder:

```powershell
./build.ps1 -GameRoot "D:\SteamLibrary\steamapps\common\Bopl Battle"
```

Build without copying the plugin into the game:

```powershell
./build.ps1 -GameRoot "D:\SteamLibrary\steamapps\common\Bopl Battle" -NoInstall
```

Use a separately extracted BepInEx pack:

```powershell
./build.ps1 `
  -GameRoot "D:\SteamLibrary\steamapps\common\Bopl Battle" `
  -BepInExPackRoot "C:\Dev\BepInEx_win_x64_5.4.23.2" `
  -NoInstall
```

The script runs all protocol/model tests before compiling `dist/BoplEight.dll`.

## Build The Installer

```powershell
./installer/build-installer.ps1 `
  -GameRoot "D:\SteamLibrary\steamapps\common\Bopl Battle" `
  -BepInExPackRoot "C:\Dev\BepInEx_win_x64_5.4.23.2"
```

The BepInEx pack root must contain `.doorstop_version`, `doorstop_config.ini`, `winhttp.dll`, and `BepInEx/core`.

The installer build:

1. Builds and tests BoplEight without installing it.
2. Runs the installer-core tests.
3. Stages the official BepInEx pack and current BoplEight DLL.
4. Tests the exact staged payload in an isolated fake game directory.
5. Embeds the payload into `installer/dist/BoplEight-Setup-1.0.0.exe`.
6. Writes a matching SHA-256 file.

## Automatic Discovery

Both scripts accept these environment variables:

- `BOPL_GAME_ROOT`: folder containing `BoplBattle.exe`.
- `BEPINEX_PACK_ROOT`: extracted BepInEx pack root.
- `BOPL_CSC_PATH`: path to `csc.exe`.

If `BOPL_GAME_ROOT` is absent, the scripts check the repository parent and registered Steam library folders.
