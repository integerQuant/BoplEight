function Resolve-CSharpCompiler {
    param([string]$ExplicitPath)

    $candidates = @(
        $ExplicitPath,
        $env:BOPL_CSC_PATH,
        "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )

    foreach ($candidate in $candidates) {
        if (![string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "The .NET Framework C# compiler was not found. Pass -Compiler or set BOPL_CSC_PATH."
}

function Resolve-BoplGameRoot {
    param(
        [string]$ExplicitPath,
        [string]$ProjectRoot
    )

    $candidates = New-Object System.Collections.Generic.List[string]

    function Add-Candidate {
        param([string]$Path)

        if ([string]::IsNullOrWhiteSpace($Path)) {
            return
        }

        try {
            $fullPath = [System.IO.Path]::GetFullPath($Path.Trim())
        }
        catch {
            return
        }

        if (!$candidates.Contains($fullPath)) {
            $candidates.Add($fullPath)
        }
    }

    Add-Candidate $ExplicitPath
    Add-Candidate $env:BOPL_GAME_ROOT
    if (![string]::IsNullOrWhiteSpace($ProjectRoot)) {
        Add-Candidate (Split-Path -Parent $ProjectRoot)
    }

    $steamRoots = New-Object System.Collections.Generic.List[string]
    $registryLocations = @(
        @{ Path = "HKCU:\Software\Valve\Steam"; Name = "SteamPath" },
        @{ Path = "HKLM:\Software\Valve\Steam"; Name = "InstallPath" },
        @{ Path = "HKLM:\Software\WOW6432Node\Valve\Steam"; Name = "InstallPath" }
    )
    foreach ($location in $registryLocations) {
        try {
            $value = (Get-ItemProperty -LiteralPath $location.Path -Name $location.Name -ErrorAction Stop).($location.Name)
            if (![string]::IsNullOrWhiteSpace($value)) {
                $steamRoots.Add([System.IO.Path]::GetFullPath($value))
            }
        }
        catch {
        }
    }

    $defaultSteam = Join-Path ${env:ProgramFiles(x86)} "Steam"
    if ((Test-Path -LiteralPath $defaultSteam) -and !$steamRoots.Contains($defaultSteam)) {
        $steamRoots.Add($defaultSteam)
    }

    $steamLibraries = New-Object System.Collections.Generic.List[string]
    foreach ($steamRoot in $steamRoots) {
        if (!$steamLibraries.Contains($steamRoot)) {
            $steamLibraries.Add($steamRoot)
        }

        $libraryFile = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (!(Test-Path -LiteralPath $libraryFile -PathType Leaf)) {
            continue
        }

        try {
            $contents = [System.IO.File]::ReadAllText($libraryFile)
            $matches = [System.Text.RegularExpressions.Regex]::Matches(
                $contents,
                '"path"\s*"([^"]+)"',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            foreach ($match in $matches) {
                $library = $match.Groups[1].Value.Replace("\\", "\").Replace("/", "\")
                if (!$steamLibraries.Contains($library)) {
                    $steamLibraries.Add($library)
                }
            }
        }
        catch {
        }
    }

    foreach ($library in $steamLibraries) {
        Add-Candidate (Join-Path $library "steamapps\common\Bopl Battle")
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "BoplBattle.exe") -PathType Leaf) {
            return $candidate
        }
    }

    throw "Bopl Battle was not found. Pass -GameRoot or set BOPL_GAME_ROOT to the folder containing BoplBattle.exe."
}

function Resolve-BepInExPackRoot {
    param(
        [string]$ExplicitPath,
        [string]$GameRoot
    )

    $candidates = @($ExplicitPath, $env:BEPINEX_PACK_ROOT, $GameRoot)
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        try {
            $fullPath = [System.IO.Path]::GetFullPath($candidate.Trim())
        }
        catch {
            continue
        }

        if (Test-Path -LiteralPath (Join-Path $fullPath "BepInEx\core\BepInEx.dll") -PathType Leaf) {
            return $fullPath
        }
    }

    throw "BepInEx 5.4.23.2 was not found. Pass -BepInExPackRoot or set BEPINEX_PACK_ROOT to an extracted BepInEx pack root."
}

function Assert-SupportedGameAssembly {
    param([string]$GameRoot)

    $expectedHash = "06A154AF64AD962E534587058219FB94216C5CE53605BB9AF5F77CB433A4AE07"
    $assemblyPath = Join-Path $GameRoot "BoplBattle_Data\Managed\Assembly-CSharp.dll"
    if (!(Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Assembly-CSharp.dll was not found under $GameRoot."
    }

    $actualHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Unsupported Bopl Battle assembly. Expected $expectedHash but found $actualHash."
    }

    return $assemblyPath
}
