using System.Diagnostics;
using System.Text;

namespace HyperXBatteryTray.Updates;

/// <summary>
/// Hands a previously verified installer to Windows PowerShell, which is a separate
/// Windows process outside the HBM installation directory. It waits for the exact
/// HBM process to exit, verifies the cached download, copies it outside the user-data
/// directory into a private Windows temporary folder, verifies that copy and then
/// launches Inno Setup. This avoids holding an executable open inside the directory
/// removed by the optional clean-install task.
/// The PowerShell script is passed inline; no writable script or helper executable
/// is left on disk or included in the Inno/MSIX package.
/// </summary>
internal static class UpdateInstallerLauncher
{
    private const string ProductionApplicationMutexName =
        "HyperBatteryMonitor_8D7E0A6C9D5B4F0B9C3E5F5E9C2A1B71";
    private const string InstallResultName = "install-result.txt";

    // PowerShell 5.1 ships with supported Windows versions. Only literals encoded as
    // base64 are inserted in this script, avoiding argument and script injection even
    // when the user's Windows profile path contains quotation marks or apostrophes.
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        function Decode([string] $value) {
            return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
        }
        $installer = Decode('INSTALLER_B64')
        $application = Decode('APPLICATION_B64')
        $resultFile = Decode('RESULT_B64')
        $readyFile = Decode('READY_B64')
        $expectedHash = 'EXPECTED_HASH'
        $expectedSize = [long]'EXPECTED_SIZE'
        $parentPid = [int]'PARENT_PID'
        $parentStartTicks = [long]'PARENT_TICKS'
        $mutexName = Decode('MUTEX_B64')
        $productionMutexName = Decode('PRODUCTION_MUTEX_B64')
        $restartArgs = Decode('RESTART_ARGS_B64')
        $isTestInstall = 'TEST_INSTALL' -eq '1'
        $outcome = 'failed'
        $stagingDirectory = $null
        [IO.File]::WriteAllText($readyFile, 'ready')
        try {
            $parent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
            if ($null -ne $parent) {
                if ($parent.StartTime.ToUniversalTime().Ticks -ne $parentStartTicks) {
                    throw 'The application PID was reused; update cancelled.'
                }
                try {
                    Wait-Process -Id $parentPid -Timeout 180 -ErrorAction Stop
                } catch {
                    # A normal shutdown can win the race between Get-Process and
                    # Wait-Process. Only continue if that exact process is gone.
                    $currentParent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
                    if ($null -ne $currentParent) { throw }
                }
            }
            # Never start Setup while another HBM instance holds the named mutex.
            try {
                $otherInstance = [Threading.Mutex]::OpenExisting($mutexName)
            } catch [Threading.WaitHandleCannotBeOpenedException] {
                $otherInstance = $null
            }
            if ($null -ne $otherInstance) {
                $otherInstance.Dispose()
                throw 'Another application instance is still running.'
            }
            # Debug test mode must not update while a production HBM instance
            # is running: its different mutex would not otherwise be detected.
            if ($isTestInstall) {
                try {
                    $productionInstance = [Threading.Mutex]::OpenExisting($productionMutexName)
                } catch [Threading.WaitHandleCannotBeOpenedException] {
                    $productionInstance = $null
                }
                if ($null -ne $productionInstance) {
                    $productionInstance.Dispose()
                    throw 'Close the production HBM before the test installation.'
                }
            }
            $file = Get-Item -LiteralPath $installer -ErrorAction Stop
            if ($file.PSIsContainer -or $file.Length -ne $expectedSize -or
                (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
                throw 'The downloaded installer is not a regular file of the expected size.'
            }
            $actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256 -ErrorAction Stop).Hash
            if ($actualHash -ine $expectedHash) {
                throw 'The downloaded installer failed its final SHA-256 check.'
            }
            # Never launch an executable from the profile directory that Clean
            # Installation must delete. The copy requires no elevation/UAC.
            $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
            $localAppData = [IO.Path]::GetFullPath(
                [Environment]::GetFolderPath('LocalApplicationData'))
            $cleanedDirs = @(
                [IO.Path]::Combine($localAppData, 'Hyper Battery Monitor'),
                [IO.Path]::Combine($localAppData, 'HyperXBatteryTray'))
            $tempParent = [IO.Path]::GetFullPath(
                [IO.Path]::Combine($tempBase, 'HyperBatteryMonitor', 'Updates'))
            foreach ($cleanedDir in $cleanedDirs) {
                $prefix = $cleanedDir.TrimEnd('\') + '\'
                if ($tempParent.Equals($cleanedDir, [StringComparison]::OrdinalIgnoreCase) -or
                    $tempParent.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Windows TEMP resolves inside an HBM user-data directory.'
                }
            }
            $stagingDirectory = [IO.Path]::Combine(
                $tempParent, [Guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
            $stagedInstaller = [IO.Path]::Combine(
                $stagingDirectory, [IO.Path]::GetFileName($installer))
            [IO.File]::Copy($installer, $stagedInstaller, $false)
            $stagedFile = Get-Item -LiteralPath $stagedInstaller -ErrorAction Stop
            if ($stagedFile.PSIsContainer -or $stagedFile.Length -ne $expectedSize -or
                (($stagedFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
                throw 'The temporary installer copy failed its size or file-type check.'
            }
            $stagedHash = (Get-FileHash -LiteralPath $stagedInstaller -Algorithm SHA256 -ErrorAction Stop).Hash
            if ($stagedHash -ine $expectedHash) {
                throw 'The temporary installer copy failed its SHA-256 check.'
            }
            # The user must still approve normal installer elevation (UAC).
            # Clean Installation remains opt-in, not silently enabled.
            $setup = Start-Process -FilePath $stagedInstaller -Verb RunAs -ArgumentList '/MERGETASKS=!cleaninstall' -Wait -PassThru -ErrorAction Stop
            if ($setup.ExitCode -ne 0) {
                throw ('Inno Setup returned exit code ' + $setup.ExitCode)
            }
            $outcome = 'success'
        } catch {
            # A declined UAC prompt or cancelled installer must not delete the cache.
            $outcome = 'failed'
        } finally {
            # Setup is no longer running after Start-Process -Wait completes.
            # If Windows still holds the file, leave it in %TEMP% for OS cleanup;
            # this must not mask the installation result or trigger another UAC.
            if ($null -ne $stagingDirectory) {
                try {
                    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction Stop
                } catch { }
            }
            if ($outcome -eq 'success') {
                try { Remove-Item -LiteralPath $resultFile -Force -ErrorAction SilentlyContinue } catch { }
            } else {
                try { [IO.File]::WriteAllText($resultFile, $outcome) } catch { }
            }
            $sameParent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
            $parentStillRunning = ($null -ne $sameParent -and
                $sameParent.StartTime.ToUniversalTime().Ticks -eq $parentStartTicks)
            $anotherInstance = $false
            try {
                $remaining = [Threading.Mutex]::OpenExisting($mutexName)
                $remaining.Dispose()
                $anotherInstance = $true
            } catch [Threading.WaitHandleCannotBeOpenedException] { }
            if ($outcome -ne 'success' -and -not $parentStillRunning -and
                -not $anotherInstance -and
                (Test-Path -LiteralPath $application -PathType Leaf)) {
                try {
                    if ([string]::IsNullOrWhiteSpace($restartArgs)) {
                        Start-Process -FilePath $application -ErrorAction Stop | Out-Null
                    } else {
                        Start-Process -FilePath $application -ArgumentList $restartArgs -ErrorAction Stop | Out-Null
                    }
                } catch { }
            }
        }
        """;

    /// <summary>
    /// Starts a separate Windows process. Does not close HBM itself; the caller
    /// must invoke the application's normal ExitThread() only if this succeeds.
    /// </summary>
    internal static async Task<bool> TryStartAsync(VerifiedInstaller installer)
    {
        if (!ApplicationRuntime.CanInstallUpdates || WindowsPackageDetector.IsPackaged() ||
            !OperatingSystem.IsWindows() ||
            !Path.IsPathFullyQualified(installer.Path) ||
            !File.Exists(installer.Path) ||
            installer.Metadata.SizeBytes <= 0 ||
            installer.Metadata.Sha256.Length != 64)
            return false;

        // Only a test-fed, previously verified installer stored in the isolated
        // test cache is eligible for --test --allowinstall handoff.
        if (ApplicationRuntime.IsTestInstallationEnabled)
        {
            string testCache = Path.GetFullPath(Path.Combine(
                ApplicationRuntime.UserDataDirectory, "Updates"));
            string installerDirectory = Path.GetFullPath(
                Path.GetDirectoryName(installer.Path)!);
            if (!string.Equals(installerDirectory, testCache,
                    StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string? application = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(application) || !File.Exists(application))
            return false;

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string powershell = Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            return false;

        using Process current = Process.GetCurrentProcess();
        string statePath = Path.Combine(Path.GetDirectoryName(installer.Path)!, InstallResultName);
        string readyPath = Path.Combine(Path.GetDirectoryName(installer.Path)!,
            ".launcher-ready-" + Guid.NewGuid().ToString("N") + ".tmp");
        string command = Script
            .Replace("INSTALLER_B64", Encode(installer.Path), StringComparison.Ordinal)
            .Replace("APPLICATION_B64", Encode(application), StringComparison.Ordinal)
            .Replace("RESULT_B64", Encode(statePath), StringComparison.Ordinal)
            .Replace("READY_B64", Encode(readyPath), StringComparison.Ordinal)
            .Replace("EXPECTED_HASH", installer.Metadata.Sha256, StringComparison.Ordinal)
            .Replace("EXPECTED_SIZE", installer.Metadata.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("PARENT_PID", current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("PARENT_TICKS", current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("PRODUCTION_MUTEX_B64", Encode(ProductionApplicationMutexName), StringComparison.Ordinal)
            .Replace("MUTEX_B64", Encode(ApplicationRuntime.MutexName), StringComparison.Ordinal)
            .Replace("RESTART_ARGS_B64", Encode(ApplicationRuntime.IsTestInstallationEnabled
                ? "--test --allowinstall" : string.Empty), StringComparison.Ordinal)
            .Replace("TEST_INSTALL", ApplicationRuntime.IsTestInstallationEnabled ? "1" : "0",
                StringComparison.Ordinal);
        // -EncodedCommand is UTF-16LE by contract, not UTF-8.
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        ProcessStartInfo start = new(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(installer.Path)!,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-WindowStyle");
        start.ArgumentList.Add("Hidden");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(encodedCommand);

        try
        {
            using Process? launcher = Process.Start(start);
            if (launcher is null) return false;
            // Wait for confirmation that the PowerShell host actually initialized.
            // Without this handshake, a blocked/crashed host would close HBM with
            // no installer or recovery path ever launched.
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (File.Exists(readyPath)) return true;
                if (launcher.HasExited) return false;
                await Task.Delay(100);
            }
            try { if (!launcher.HasExited) launcher.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
            InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(readyPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
