using System.Runtime.InteropServices;
using System.Text;

namespace HyperXBatteryTray.Updates;

/// <summary>
/// Packaged/MSIX installs are managed by Windows. A failed package-status query is
/// conservatively treated as packaged so we never offer an Inno installer to MSIX.
/// </summary>
internal static class WindowsPackageDetector
{
    private const int AppModelErrorNoPackage = 15700;


    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? packageFullName);

    internal static bool IsPackaged()
    {
        try
        {
            uint length = 0;
            int result = GetCurrentPackageFullName(ref length, null);
            if (result == AppModelErrorNoPackage)
                return false;
            // ERROR_INSUFFICIENT_BUFFER (122) means packaged. Other errors
            // fail closed so an MSIX process never executes a GitHub installer.
            return true;
        }
        catch (EntryPointNotFoundException) { return true; }
        catch (DllNotFoundException) { return true; }
    }
}
