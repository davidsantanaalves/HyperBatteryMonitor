using Microsoft.Win32;

namespace HyperXBatteryTray;

public sealed class StartupManager
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string AppName =
        "HyperBatteryMonitor";

    private const string LegacyAppName =
        "HyperXBatteryMonitor";

    private const string OlderLegacyAppName =
        "HyperXBatteryTray";

    public bool IsEnabled()
    {
        if (ApplicationRuntime.IsTestMode)
            return false;

        using RegistryKey? key =
            Registry.CurrentUser.OpenSubKey(RunKey);

        if (key == null)
            return false;

        return HasStartupEntry(key, AppName) ||
               HasStartupEntry(key, LegacyAppName) ||
               HasStartupEntry(key, OlderLegacyAppName);
    }

    public void MigrateLegacyRegistration()
    {
        if (ApplicationRuntime.IsTestMode)
            return;

        using RegistryKey? readKey =
            Registry.CurrentUser.OpenSubKey(RunKey);

        bool wasEnabled =
            readKey != null &&
            (HasStartupEntry(readKey, AppName) ||
             HasStartupEntry(readKey, LegacyAppName) ||
             HasStartupEntry(readKey, OlderLegacyAppName));

        if (!wasEnabled)
            return;

        Enable();
    }

    public void Enable()
    {
        if (ApplicationRuntime.IsTestMode)
            throw new InvalidOperationException("Windows startup registration is unavailable in test mode.");

        string executablePath = Application.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException(
                "Não foi possível determinar o caminho do executável.");

        if (!File.Exists(executablePath))
            throw new FileNotFoundException(
                "O executável da aplicação não foi encontrado.",
                executablePath);

        using RegistryKey key =
            Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException(
                "Não foi possível acessar a chave de inicialização do Windows.");

        string command = $"\"{executablePath}\"";

        key.SetValue(
            AppName,
            command,
            RegistryValueKind.String);

        key.DeleteValue(
            LegacyAppName,
            throwOnMissingValue: false);
        key.DeleteValue(
            OlderLegacyAppName,
            throwOnMissingValue: false);

        object? savedValue = key.GetValue(AppName);

        if (savedValue is not string savedCommand ||
            !savedCommand.Equals(
                command,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A configuração de inicialização não pôde ser confirmada no Registro do Windows.");
        }
    }

    public void Disable()
    {
        if (ApplicationRuntime.IsTestMode)
            return; // Never remove production startup entries from test mode.

        using RegistryKey? key =
            Registry.CurrentUser.OpenSubKey(
                RunKey,
                writable: true);

        key?.DeleteValue(
            AppName,
            throwOnMissingValue: false);
        key?.DeleteValue(
            LegacyAppName,
            throwOnMissingValue: false);
        key?.DeleteValue(
            OlderLegacyAppName,
            throwOnMissingValue: false);
    }

    private static bool HasStartupEntry(
        RegistryKey key,
        string valueName)
    {
        return key.GetValue(valueName) is string command &&
               !string.IsNullOrWhiteSpace(command);
    }
}
