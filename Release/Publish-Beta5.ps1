# Hyper Battery Monitor v2.4.0-beta.5 - guarded GitHub prerelease publication.
# Usage: .\Release\Publish-Beta5.ps1             (validate/build only)
#        .\Release\Publish-Beta5.ps1 -Publish    (validate/build/publish after confirmation)
[CmdletBinding()]
param([switch]$Publish)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$repo = 'davidsantanaalves/HyperBatteryMonitor'
$branch = 'develop/v2.4.0'
$tag = 'v2.4.0-beta.5'
$setupName = 'HyperBatteryMonitor-Setup-v2.4.0-beta.5.exe'
$setupPath = Join-Path $root ('Releases\' + $setupName)
$notesPath = Join-Path $root 'Release\ReleaseNotes-v2.4.0-beta.5.md'
$projectPath = Join-Path $root 'HyperXBatteryMonitor.csproj'

function Run-Native {
    param([string]$Label, [scriptblock]$Action)
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit code $LASTEXITCODE)." }
}

foreach ($cmd in @('git', 'gh', 'dotnet')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $cmd"
    }
}

Push-Location $root
try {
    # Select exact XML elements instead of accessing optional PropertyGroup members.
    # PowerShell StrictMode throws when any other PropertyGroup has no Version property.
    [xml]$csproj = Get-Content -LiteralPath $projectPath -Raw
    $versionNodes = @($csproj.SelectNodes('/Project/PropertyGroup/Version') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $channelNodes = @($csproj.SelectNodes('/Project/PropertyGroup/ReleaseChannel') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $betaNodes = @($csproj.SelectNodes('/Project/PropertyGroup/BetaNumber') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($versionNodes.Count -ne 1 -or $channelNodes.Count -ne 1 -or $betaNodes.Count -ne 1) {
        throw 'Expected exactly one nonempty Version, ReleaseChannel and BetaNumber property in the project.'
    }
    if ($versionNodes[0] -cne '2.4.0' -or
        $channelNodes[0] -cne 'Beta' -or $betaNodes[0] -cne '5') {
        throw 'Expected csproj: Version=2.4.0; ReleaseChannel=Beta; BetaNumber=5.'
    }
    if (-not (Test-Path -LiteralPath $notesPath)) { throw "Missing release notes: $notesPath" }
    $notes = Get-Content -LiteralPath $notesPath -Raw
    if ($notes -match '(?i)development candidate|latest published pre-release until') {
        throw 'Beta 5 release notes still contain pre-publication candidate wording.'
    }
    foreach ($requiredNotes in @('Clean Installation', 'Cloud Stinger Core', 'Adaptive HID', '03F0:02CC', '03F0:0A93', 'PR #6')) {
        if (-not $notes.Contains($requiredNotes)) { throw "Release notes missing required cumulative Beta 4/5 scope: $requiredNotes" }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'Settings\ThemedVerticalScrollBar.cs'))) {
        throw 'The themed scrollbar patch is missing.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'Updates\UpdateChannelPolicy.cs'))) {
        throw 'The Stable/Beta rollback patch is missing.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'Settings\UpdateReleaseNotesDialog.cs'))) {
        throw 'The Whats new patch is missing.'
    }
    # Preserve the Beta 4 REV2 SetupAPI HID path-offset correction.
    $hidPath = Join-Path $root 'Hid\HidConnection.cs'
    $hidSource = Get-Content -LiteralPath $hidPath -Raw
    if (-not $hidSource.Contains('DEVICE_INTERFACE_PATH_OFFSET = sizeof(int)') -or
        -not $hidSource.Contains('DEVICE_INTERFACE_PATH_OFFSET));')) {
        throw 'Beta 4 REV2 HID path-offset fix missing; do not publish Beta 5.'
    }
    $resolverPath = Join-Path $root 'Devices\ReceiverAwareDevice.cs'
    $resolverSource = Get-Content -LiteralPath $resolverPath -Raw
    if (-not $resolverSource.Contains('EnumerateControlEndpoints') -or
        -not $resolverSource.Contains('HidEndpointPreferenceStore.Remember')) {
        throw 'Beta 4 adaptive HID resolver is missing or not the expected implementation.'
    }

    # Beta 5 clean-installation logic must not return failure due to startup Registry migration.
    $programSource = Get-Content -LiteralPath (Join-Path $root 'Program.cs') -Raw
    $settingsSource = Get-Content -LiteralPath (Join-Path $root 'Settings\SettingsManager.cs') -Raw
    $installerSource = Get-Content -LiteralPath (Join-Path $root 'Installer\HyperXBatteryTray.iss') -Raw
    if (-not $programSource.Contains('HBM-Maintenance.log') -or
        -not $programSource.Contains('RunUserDataMaintenance') -or
        -not $settingsSource.Contains('DeleteDirectoryIfExists') -or
        -not $installerSource.Contains('Maintenance process exit code')) {
        throw 'Beta 5 clean-installation reliability/diagnostic fix is missing.'
    }

    # PR #6-based PlayStation support must be receiver-specific and credited.
    $stingerSource = Get-Content -LiteralPath (Join-Path $root 'Devices\CloudStingerCoreWirelessDevice.cs') -Raw
    $managerSource = Get-Content -LiteralPath (Join-Path $root 'Devices\HyperXDeviceManager.cs') -Raw
    $readmeSource = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
    if (-not $stingerSource.Contains('PlayStationDefinition') -or
        -not $stingerSource.Contains('PlayStationBatteryResponseAttempts') -or
        -not $stingerSource.Contains('isExpectedResponse == null ? 1 : PlayStationBatteryResponseAttempts') -or
        -not $managerSource.Contains('CloudStingerCoreWirelessDevice.PlayStationDefinition') -or
        -not $readmeSource.Contains('HyperBatteryMonitor/pull/6')) {
        throw 'Beta 5 Stinger Core PlayStation implementation or README credit missing.'
    }

    # Every critical Beta 5 component must be tracked in the release commit,
    # not merely exist as untracked files in the working directory.
    foreach ($trackedPath in @(
        'Settings/ThemedVerticalScrollBar.cs',
        'Settings/UpdateReleaseNotesDialog.cs',
        'Updates/UpdateChannelPolicy.cs',
        'Release/ReleaseNotes-v2.4.0-beta.5.md',
        'Release/Publish-Beta5.ps1',
        'Program.cs',
        'Settings/SettingsManager.cs',
        'Installer/HyperXBatteryTray.iss',
        'HyperXBatteryMonitor.csproj',
        'Devices/CloudStingerCoreWirelessDevice.cs',
        'Devices/HyperXDeviceManager.cs',
        'README.md',
        'Devices/HidEndpointPreferenceStore.cs',
        'Devices/ISelectableHidEndpoint.cs',
        'Devices/ReceiverAwareDevice.cs',
        'Hid/HidConnection.cs',
        'Devices/Cloud3SAlternateReceiverDevice.cs'
    )) {
        $trackedResult = & git ls-files --error-unmatch -- $trackedPath 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $trackedResult) {
            throw "Required release file is not tracked by git: $trackedPath"
        }
    }

    $actualBranch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualBranch -cne $branch) {
        throw "Expected git branch '$branch'; actual '$actualBranch'."
    }
    $remote = (& git remote get-url origin).Trim()
    if ($LASTEXITCODE -ne 0 -or $remote -notmatch 'github\.com[:/]davidsantanaalves/HyperBatteryMonitor(?:\.git)?$') {
        throw "Unexpected origin remote: $remote"
    }

    $status = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        Write-Host 'Git working tree is not clean. Review and commit all Beta 5 changes first:' -ForegroundColor Yellow
        git status --short
        throw 'Publication aborted: uncommitted or untracked files.'
    }
    $localTags = @(git tag --list $tag)
    if ($LASTEXITCODE -ne 0 -or $localTags.Count -gt 0) { throw "Local tag $tag already exists. Reconcile before continuing." }

    Run-Native 'git fetch' { git fetch origin $branch }
    $sha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not read local git HEAD.' }
    $remoteSha = (& git rev-parse "origin/$branch").Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -cne $remoteSha) {
        throw "Local HEAD $sha differs from origin/$branch $remoteSha. Push/reconcile before publication."
    }

    $remoteTag = @(git ls-remote --tags origin "refs/tags/$tag")
    if ($LASTEXITCODE -ne 0 -or $remoteTag.Count -gt 0) { throw "Remote tag $tag already exists or could not be checked." }

    $releasesJson = & gh release list --repo $repo --limit 100 --json tagName
    if ($LASTEXITCODE -ne 0) { throw 'Cannot query existing GitHub releases.' }
    $releases = @($releasesJson | ConvertFrom-Json)
    if (@($releases | Where-Object { $_.tagName -ceq $tag }).Count -gt 0) {
        throw "GitHub release $tag already exists. Do not overwrite a published version."
    }

    Write-Host "Verified HEAD: $sha" -ForegroundColor Cyan
    Write-Host "Building $tag ..." -ForegroundColor Cyan
    Run-Native 'dotnet build Release' { dotnet build .\HyperXBatteryMonitor.csproj -c Release }

    $builder = Join-Path $root 'Release\Build-Release.ps1'
    if (-not (Test-Path $builder)) { throw "Missing build script $builder" }
    & $builder
    if (-not (Test-Path -LiteralPath $setupPath)) { throw "Missing installer $setupPath" }
    $setupFile = Get-Item -LiteralPath $setupPath
    if ($setupFile.Length -le 0) { throw "Installer is empty: $setupPath" }
    $sha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()

    # The installer must correspond to the exact reviewed and pushed commit.
    # Build scripts should not modify tracked source/version/packaging files.
    $afterBuildStatus = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $afterBuildStatus.Count -ne 0) {
        git status --short
        throw 'Build changed the Git working tree. Review/revert these changes before publishing.'
    }
    $afterBuildSha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $afterBuildSha -cne $sha) {
        throw 'Git HEAD changed during build. Refusing to publish an installer from an unverified commit.'
    }

    Write-Host "Installer: $($setupFile.FullName)" -ForegroundColor Green
    Write-Host "Size: $($setupFile.Length) bytes"
    Write-Host "SHA-256: $sha256"
    Write-Host "Release notes: $notesPath"
    Write-Warning 'KNOWN LIMITATIONS: Stinger Core PlayStation integration is not yet verified end-to-end on its hardware; percentage may be high during charging. See release notes.'

    if (-not $Publish) {
        Write-Host 'PREPARATION COMPLETE - NOTHING PUBLISHED.' -ForegroundColor Green
        Write-Host 'Run the same script with -Publish after reviewing the installer and release notes.'
        return
    }

    $confirm = Read-Host "To publish $tag to $repo at commit $sha type PUBLISH"
    if ($confirm -cne 'PUBLISH') { throw 'Publication canceled by user.' }

    Run-Native 'GitHub release create' {
        gh release create $tag $setupPath --repo $repo --target $sha `
            --title 'Hyper Battery Monitor v2.4.0 Beta 5' `
            --prerelease --latest=false --notes-file $notesPath
    }

    $verifyJson = & gh api "repos/$repo/releases/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw 'Published release exists but could not be verified.' }
    $published = $verifyJson | ConvertFrom-Json
    if ($published.draft -or -not $published.prerelease -or $published.tag_name -cne $tag) {
        throw 'Published release flags/tag do not match the expected Beta 5 prerelease.'
    }
    $matches = @($published.assets | Where-Object { $_.name -ceq $setupName -and $_.state -eq 'uploaded' })
    if ($matches.Count -ne 1) { throw 'Published release does not include one uploaded expected installer.' }
    if ([long]$matches[0].size -ne [long]$setupFile.Length) {
        throw 'Published installer size differs from the local installer.'
    }
    if ($matches[0].digest -cne "sha256:$sha256") {
        throw "Published installer SHA mismatch or missing GitHub digest. Local $sha256 ; GitHub $($matches[0].digest)"
    }
    $gitTagJson = & gh api "repos/$repo/git/ref/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw 'Published tag could not be verified through GitHub.' }
    $gitTag = $gitTagJson | ConvertFrom-Json
    if ($gitTag.object.type -cne 'commit' -or $gitTag.object.sha -cne $sha) {
        throw "Release tag does not point directly to expected commit $sha. Inspect before proceeding."
    }
    Write-Host 'BETA 5 PUBLISHED AND VERIFIED.' -ForegroundColor Green
    Write-Host "Release URL: $($published.html_url)"
    Write-Host "Asset SHA-256: $sha256"
}
finally {
    Pop-Location
}
