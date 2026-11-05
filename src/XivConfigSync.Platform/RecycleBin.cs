using System.Runtime.InteropServices;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Platform;

/// <summary>
/// Moves files to the Windows Recycle Bin through the shell, so whatever is removed can be put
/// back from there. Where a file cannot be recycled (a drive without a Recycle Bin), the shell
/// asks before it would delete it for good, and a file that is still there afterwards is reported
/// as not moved.
/// </summary>
public sealed class RecycleBin : IRecycleBin
{
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    public void Recycle(string fullPath)
    {
        if (!Path.IsPathFullyQualified(fullPath) || !File.Exists(fullPath))
        {
            throw new IOException("Not an existing file: " + Path.GetFileName(fullPath));
        }

        var operation = new NativeMethods.ShFileOpStruct
        {
            Func = FoDelete,
            // The shell takes a list ended by an empty entry: two terminating characters.
            From = fullPath + "\0\0",
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning,
        };
        var result = NativeMethods.SHFileOperation(ref operation);
        if (result != 0)
        {
            throw new IOException($"The shell could not move the file to the Recycle Bin (code {result}).");
        }

        if (operation.AnyOperationsAborted || File.Exists(fullPath))
        {
            throw new InvalidOperationException("The file was not moved to the Recycle Bin.");
        }
    }

    private static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ShFileOpStruct
        {
            public IntPtr Window;
            public uint Func;
            public string From;
            public string? To;
            public ushort Flags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool AnyOperationsAborted;
            public IntPtr NameMappings;
            public string? ProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int SHFileOperation(ref ShFileOpStruct operation);
    }
}
