param(
    [string]$GameRoot,
    [string]$BepInExPackRoot,
    [string]$Compiler
)

$ErrorActionPreference = "Stop"
$installerVersion = "1.0.6"
$projectRoot = Split-Path -Parent $PSScriptRoot
$installerRoot = $PSScriptRoot
$helpers = Join-Path $projectRoot "scripts\BuildHelpers.ps1"
. $helpers

$gameRoot = Resolve-BoplGameRoot -ExplicitPath $GameRoot -ProjectRoot $projectRoot
$bepInExRoot = Resolve-BepInExPackRoot -ExplicitPath $BepInExPackRoot -GameRoot $gameRoot
$compilerPath = Resolve-CSharpCompiler -ExplicitPath $Compiler
$gameAssembly = Assert-SupportedGameAssembly -GameRoot $gameRoot
$objectRoot = Join-Path $installerRoot "obj"
$payloadRoot = Join-Path $objectRoot "payload"
$payloadZip = Join-Path $objectRoot "BoplEight.Payload.zip"
$outputRoot = Join-Path $installerRoot "dist"
$outputExe = Join-Path $outputRoot "BoplEight-Setup-$installerVersion.exe"
$testExe = Join-Path $objectRoot "BoplEight.InstallerTests.exe"
$payloadTestExe = Join-Path $objectRoot "BoplEight.PayloadIntegrationTests.exe"
$plugin = Join-Path $projectRoot "dist\BoplEight.dll"
$bepInExCore = Join-Path $bepInExRoot "BepInEx\core"

& (Join-Path $projectRoot "build.ps1") -GameRoot $gameRoot -BepInExPackRoot $bepInExRoot -Compiler $compilerPath -NoInstall
if ($LASTEXITCODE -ne 0) {
    throw "BoplEight build failed."
}

if (Test-Path -LiteralPath $objectRoot) {
    Remove-Item -LiteralPath $objectRoot -Recurse -Force
}
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$compressionReferences = @(
    "/reference:System.IO.Compression.dll",
    "/reference:System.IO.Compression.FileSystem.dll"
)

& $compilerPath /nologo /target:exe "/out:$testExe" $compressionReferences `
    (Join-Path $installerRoot "src\InstallerCore.cs") `
    (Join-Path $installerRoot "tests\InstallerCoreTests.cs")
if ($LASTEXITCODE -ne 0) {
    throw "Installer test build failed."
}

& $testExe $gameAssembly
if ($LASTEXITCODE -ne 0) {
    throw "Installer tests failed."
}

Copy-Item -LiteralPath (Join-Path $bepInExRoot ".doorstop_version") -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $bepInExRoot "doorstop_config.ini") -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $bepInExRoot "winhttp.dll") -Destination $payloadRoot
New-Item -ItemType Directory -Path (Join-Path $payloadRoot "BepInEx\core") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $payloadRoot "BepInEx\plugins") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $payloadRoot "BepInEx\BoplEight") -Force | Out-Null
Copy-Item -Path (Join-Path $bepInExCore "*") -Destination (Join-Path $payloadRoot "BepInEx\core") -Recurse -Force
Copy-Item -LiteralPath $plugin -Destination (Join-Path $payloadRoot "BepInEx\plugins\BoplEight.dll")
Copy-Item -Path (Join-Path $installerRoot "payload-info\*") -Destination (Join-Path $payloadRoot "BepInEx\BoplEight") -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $payloadRoot,
    $payloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

& $compilerPath /nologo /target:exe "/out:$payloadTestExe" $compressionReferences `
    (Join-Path $installerRoot "src\InstallerCore.cs") `
    (Join-Path $installerRoot "tests\PayloadIntegrationTests.cs")
if ($LASTEXITCODE -ne 0) {
    throw "Payload integration test build failed."
}

& $payloadTestExe $payloadZip $gameAssembly $plugin
if ($LASTEXITCODE -ne 0) {
    throw "Payload integration test failed."
}

& $compilerPath /nologo /target:winexe "/out:$outputExe" `
    "/win32manifest:$(Join-Path $installerRoot 'app.manifest')" `
    "/resource:$payloadZip,BoplEight.Payload.zip" `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $compressionReferences `
    (Join-Path $installerRoot "src\InstallerCore.cs") `
    (Join-Path $installerRoot "src\InstallerForm.cs") `
    (Join-Path $installerRoot "src\Program.cs")
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

$hash = (Get-FileHash -LiteralPath $outputExe -Algorithm SHA256).Hash
$hashFile = Join-Path $outputRoot "BoplEight-Setup-$installerVersion.sha256.txt"
Set-Content -LiteralPath $hashFile -Value "$hash  BoplEight-Setup-$installerVersion.exe" -Encoding ASCII

"Built $outputExe"
"SHA-256 $hash"
