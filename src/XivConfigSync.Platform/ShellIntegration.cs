using System.Runtime.InteropServices;
using Microsoft.Win32;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Platform;

/// <summary>
/// Autostart and the <c>.xivsync</c> file association for the current user only
/// (<c>HKCU</c>, no administrator rights). Both are off until the user turns them on (X-08).
/// </summary>
public sealed partial class ShellIntegration : IShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "XIV ConfigSync";
    private const string ProgId = "XivConfigSync.SyncFile";
    private const string Extension = ".xivsync";

    private readonly string executable;
    private readonly string fileTypeName;

    public ShellIntegration(string executable, string fileTypeName)
    {
        this.executable = executable;
        this.fileTypeName = fileTypeName;
    }

    public bool IsAutostartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string value && value.Contains(executable, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsFileAssociationRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            return key?.GetValue(null) is string value && value.Contains(executable, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(RunValue, $"\"{executable}\" --tray");
        }
        else
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    public void SetFileAssociation(bool enabled)
    {
        if (enabled)
        {
            using (var ext = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Extension}"))
            {
                ext.SetValue(null, ProgId);
            }

            using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                prog.SetValue(null, fileTypeName);
                using var icon = prog.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"\"{executable}\",0");
                using var command = prog.CreateSubKey(@"shell\open\command");
                command.SetValue(null, $"\"{executable}\" \"%1\"");
            }
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            using var ext = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Extension}", writable: true);
            if (ext?.GetValue(null) as string == ProgId)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Extension}", throwOnMissingSubKey: false);
            }
        }

        // Tell the shell that associations changed, so icons update without a sign-out.
        NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static partial class NativeMethods
    {
        [DllImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
