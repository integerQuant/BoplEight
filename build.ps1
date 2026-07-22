param(
    [string]$GameRoot,
    [string]$BepInExPackRoot,
    [string]$Compiler,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$helpers = Join-Path $projectRoot "scripts\BuildHelpers.ps1"
. $helpers

$gameRoot = Resolve-BoplGameRoot -ExplicitPath $GameRoot -ProjectRoot $projectRoot
$bepInExRoot = Resolve-BepInExPackRoot -ExplicitPath $BepInExPackRoot -GameRoot $gameRoot
$compilerPath = Resolve-CSharpCompiler -ExplicitPath $Compiler
$gameAssembly = Assert-SupportedGameAssembly -GameRoot $gameRoot
$dist = Join-Path $projectRoot "dist"
$managed = Join-Path $gameRoot "BoplBattle_Data\Managed"
$core = Join-Path $bepInExRoot "BepInEx\core"
$pluginDirectory = Join-Path $gameRoot "BepInEx\plugins"

New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (!$NoInstall) {
    New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
}

$protocolSources = @(
    (Join-Path $projectRoot "src\Protocol\PacketCodec.cs"),
    (Join-Path $projectRoot "src\Protocol\PacketRouter.cs"),
    (Join-Path $projectRoot "src\Lobby\LobbyJoinGate.cs"),
    (Join-Path $projectRoot "src\Lobby\LobbyMetadata.cs"),
    (Join-Path $projectRoot "src\Lobby\PendingLobbyJoinState.cs"),
    (Join-Path $projectRoot "src\Colors\TeamPalette.cs"),
    (Join-Path $projectRoot "src\Match\RosterModel.cs"),
    (Join-Path $projectRoot "src\Ui\RosterLayout.cs")
)

$testSources = $protocolSources + @(
    (Join-Path $projectRoot "tests\ProtocolTests.cs"),
    (Join-Path $projectRoot "tests\RuntimeModelTests.cs"),
    (Join-Path $projectRoot "tests\MatchCompatibilityTests.cs"),
    (Join-Path $projectRoot "tests\PaletteTests.cs")
)

& $compilerPath /nologo /target:exe "/out:$dist\BoplEight.ProtocolTests.exe" $testSources
if ($LASTEXITCODE -ne 0) {
    throw "Protocol test build failed."
}

& "$dist\BoplEight.ProtocolTests.exe"
if ($LASTEXITCODE -ne 0) {
    throw "Protocol tests failed."
}

$runtimeSources = $protocolSources + @(
    (Join-Path $projectRoot "src\Runtime\BoplEightPlugin.cs"),
    (Join-Path $projectRoot "src\Runtime\PaletteRuntime.cs"),
    (Join-Path $projectRoot "src\Runtime\RosterRuntime.cs"),
    (Join-Path $projectRoot "src\Runtime\FrameRuntime.cs"),
    (Join-Path $projectRoot "src\Runtime\SpawnRuntime.cs"),
    (Join-Path $projectRoot "src\Runtime\LobbyUiRuntime.cs"),
    (Join-Path $projectRoot "src\Runtime\GameplayRuntime.cs"),
    (Join-Path $projectRoot "src\Runtime\PeerCompatibility.cs"),
    (Join-Path $projectRoot "src\Runtime\RosterStartCoordinator.cs"),
    (Join-Path $projectRoot "src\Runtime\AssetManifest.cs")
)

& $compilerPath /nologo /target:library "/out:$dist\BoplEight.dll" "/reference:$core\BepInEx.dll" "/reference:$core\0Harmony.dll" "/reference:$gameAssembly" "/reference:$managed\Facepunch.Steamworks.Win64.dll" "/reference:$managed\UnityEngine.dll" "/reference:$managed\UnityEngine.CoreModule.dll" "/reference:$managed\UnityEngine.UIModule.dll" "/reference:$managed\UnityEngine.UI.dll" "/reference:$managed\Unity.InputSystem.dll" "/reference:$managed\Unity.TextMeshPro.dll" "/reference:$managed\netstandard.dll" $runtimeSources
if ($LASTEXITCODE -ne 0) {
    throw "Plugin build failed."
}

if (!$NoInstall) {
    Copy-Item -LiteralPath "$dist\BoplEight.dll" -Destination "$pluginDirectory\BoplEight.dll" -Force
    "Installed $pluginDirectory\BoplEight.dll"
}
else {
    "Built $dist\BoplEight.dll without installing it."
}
