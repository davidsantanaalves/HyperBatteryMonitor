param(
    [switch]$BuildStorePackage,
    [string]$Prerelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'HyperXBatteryMonitor.csproj'
$installer = Join-Path $root 'Installer\HyperXBatteryTray.iss'

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$versionNodes = @(
    $projectXml.SelectNodes('/Project/PropertyGroup/Version') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

if ($versionNodes.Count -ne 1) {
    throw "Expected exactly one <Version> element in $project, but found $($versionNodes.Count)."
}

$baseVersion = ([string]$versionNodes[0]).Trim()
if ($baseVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "The project version '$baseVersion' is not a numeric release version supported by the installer."
}

$versionParts = @($baseVersion.Split('.') | ForEach-Object { [int]$_ })
if ($versionParts.Count -eq 3) {
    $versionParts += 0
}

if ($versionParts.Count -ne 4 -or @($versionParts | Where-Object { $_ -lt 0 -or $_ -gt 65535 }).Count -ne 0) {
    throw "The project version '$baseVersion' cannot be converted to a valid four-part MSIX version."
}

$numericVersion = ($versionParts -join '.')

$channelNodes = @(
    $projectXml.SelectNodes('/Project/PropertyGroup/ReleaseChannel') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($channelNodes.Count -ne 1) {
    throw "Expected exactly one <ReleaseChannel> property in $project."
}
$releaseChannel = ([string]$channelNodes[0]).Trim()
if ($releaseChannel -cnotin @('Stable', 'Beta')) {
    throw "Invalid ReleaseChannel '$releaseChannel'. Expected Stable or Beta."
}

$releaseVersion = $baseVersion
if ($releaseChannel -ceq 'Beta') {
    $betaNodes = @(
        $projectXml.SelectNodes('/Project/PropertyGroup/BetaNumber') |
            ForEach-Object { $_.InnerText.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($betaNodes.Count -ne 1) {
        throw "Expected exactly one <BetaNumber> property in $project."
    }
    $betaNumber = ([string]$betaNodes[0]).Trim()
    if ($betaNumber -cnotmatch '^[1-9][0-9]*$') {
        throw "Invalid BetaNumber '$betaNumber'. Use 1, 2, 3, ... without leading zeros."
    }
    if ($baseVersion.Split('.').Count -ne 3) {
        throw 'Beta builds require a three-part SemVer base version.'
    }
    $releaseVersion = "$baseVersion-beta.$betaNumber"
}

# Preserve the historical -Prerelease option as a consistency check only.
# It must not override the .csproj source of truth.
if ($PSBoundParameters.ContainsKey('Prerelease') -and
    "$baseVersion-$Prerelease" -cne $releaseVersion) {
    throw "-Prerelease '$Prerelease' conflicts with ReleaseChannel/BetaNumber in the .csproj ($releaseVersion)."
}

$storePackageVersion = $numericVersion
$packageProject = Join-Path $root 'Packaging\HyperXBatteryMonitor.Package.wapproj'
$packageManifest = Join-Path $root 'Packaging\Package.appxmanifest'
# A fresh publish directory prevents stale assets from earlier builds entering the installer.
$publish = Join-Path $root ("bin\Release\release-staging\" + [Guid]::NewGuid().ToString('N') + '\publish')
$release = Join-Path $root 'Releases'

New-Item -ItemType Directory -Force -Path $release | Out-Null

Write-Host "Publishing Hyper Battery Monitor $releaseVersion..." -ForegroundColor Cyan
$versionProperties = @(
    "-p:Version=$baseVersion",
    "-p:InformationalVersion=$releaseVersion",
    "-p:AssemblyVersion=$numericVersion",
    "-p:FileVersion=$numericVersion",
    '-p:IncludeSourceRevisionInInformationalVersion=false'
)
dotnet publish $project -c Release -r win-x64 --self-contained true --output $publish -p:DebugType=None -p:DebugSymbols=false @versionProperties
if ($LASTEXITCODE -ne 0) {
    throw "Application publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $publish 'Hyper Battery Monitor.exe'
if (-not (Test-Path $exe)) {
    throw "Published executable was not found: $exe"
}

# Locate Inno Setup without dereferencing a null Get-Command result.
$isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
$isccCandidates = @()
if ($isccCommand) {
    $isccCandidates += $isccCommand.Source
}
$isccCandidates += @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }
$isccCandidates = @($isccCandidates)

if ($isccCandidates.Count -eq 0) {
    throw 'Inno Setup (ISCC.exe) was not found. Expected Inno Setup 7 or 6.'
}

Write-Host 'Building installer...' -ForegroundColor Cyan
& $isccCandidates[0] "/DMyAppVersion=$releaseVersion" "/DMyAppNumericVersion=$numericVersion" "/DMyPublishDir=$publish" $installer
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE."
}

$installerOutput = Join-Path $release "HyperBatteryMonitor-Setup-v$releaseVersion.exe"
if (-not (Test-Path -LiteralPath $installerOutput) -or (Get-Item -LiteralPath $installerOutput).Length -eq 0) {
    throw "Expected installer was not generated: $installerOutput"
}

if ($BuildStorePackage) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        $vswhere = Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe'
    }

    $msbuildCandidates = @(
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    $msbuild = $msbuildCandidates | Select-Object -First 1

    if (-not $msbuild -and (Test-Path $vswhere)) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe |
            Where-Object { $_ -and (Test-Path $_) } |
            Select-Object -First 1
    }

    if (-not $msbuild) {
        $msbuildCommand = Get-Command msbuild.exe -ErrorAction SilentlyContinue
        if ($msbuildCommand) {
            $msbuild = $msbuildCommand.Source
        }
    }

    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw 'MSBuild.exe was not found. Install Visual Studio with the Windows Application Packaging Project workload.'
    }

    if (-not (Test-Path $packageManifest)) {
        throw "Package manifest was not found: $packageManifest"
    }

    Write-Host "Building Microsoft Store MSIX package $storePackageVersion..." -ForegroundColor Cyan

    # Package.appxmanifest requires a four-part numeric Identity version.
    # HyperXBatteryMonitor.csproj remains the single version source; the manifest is
    # updated only for the duration of this build and restored byte-for-byte afterwards.
    $originalManifestBytes = [System.IO.File]::ReadAllBytes($packageManifest)
    try {
        [xml]$manifestXml = Get-Content -LiteralPath $packageManifest -Raw
        $identityNode = $manifestXml.Package.Identity
        if (-not $identityNode) {
            throw "The package manifest does not contain a Package/Identity element: $packageManifest"
        }

        $identityNode.Version = $storePackageVersion

        $writerSettings = [System.Xml.XmlWriterSettings]::new()
        $writerSettings.Indent = $true
        $writerSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
        $writerSettings.NewLineChars = "`r`n"
        $writerSettings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

        $writer = [System.Xml.XmlWriter]::Create($packageManifest, $writerSettings)
        try {
            $manifestXml.Save($writer)
        }
        finally {
            $writer.Dispose()
        }

        # Rebuild to prevent cached upload manifests from retaining a previous release version.
        # AppxPackageVersion is passed explicitly as an additional guard so MSBuild and the
        # manifest agree on the exact version being packaged.
        & $msbuild $packageProject /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:SolutionDir="$root\" /p:AppxPackageVersion=$storePackageVersion /p:Version=$baseVersion /p:InformationalVersion=$releaseVersion /p:AssemblyVersion=$numericVersion /p:FileVersion=$numericVersion /p:IncludeSourceRevisionInInformationalVersion=false
        if ($LASTEXITCODE -ne 0) {
            throw "Store package build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        [System.IO.File]::WriteAllBytes($packageManifest, $originalManifestBytes)
    }
}

Write-Host ''
Write-Host "Published application: $publish"
Write-Host "Release artifacts are in: $release" -ForegroundColor Green
