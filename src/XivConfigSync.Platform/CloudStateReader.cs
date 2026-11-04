using System.Reflection;
using System.Runtime.InteropServices;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Platform;

/// <summary>
/// Reads the shell property <c>System.StorageProviderState</c> through the shell's automation
/// object, the same way the gate 1 measurement did, without opening the file (opening it could
/// make the client download a placeholder). Values measured on OneDrive for Business: 2 in sync,
/// 4 new and pending, 10 changed and pending. Anything else, or no value, is unknown.
/// </summary>
public sealed class CloudStateReader : ICloudStateReader
{
    private const string Property = "System.StorageProviderState";

    public CloudFileState GetState(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(full);
            if (folder is null || !File.Exists(full))
            {
                return CloudFileState.Unknown;
            }

            return Interpret(ReadProperty(folder, Path.GetFileName(full)));
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidCastException or IOException or UnauthorizedAccessException)
        {
            return CloudFileState.Unknown;
        }
    }

    /// <summary>Maps a raw property value to a state.</summary>
    public static CloudFileState Interpret(object? value) =>
        value switch
        {
            null => CloudFileState.Unknown,
            _ when Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == 2 => CloudFileState.InSync,
            _ when Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) is 4 or 10 => CloudFileState.Pending,
            _ => CloudFileState.Unknown,
        };

    /// <summary>The raw property value, for diagnostics.</summary>
    public static object? ReadProperty(string folder, string fileName)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return null;
        }

        var shell = Activator.CreateInstance(shellType)!;
        object? shellFolder = null;
        object? item = null;
        try
        {
            shellFolder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, [folder], System.Globalization.CultureInfo.InvariantCulture);
            if (shellFolder is null)
            {
                return null;
            }

            item = shellFolder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, shellFolder, [fileName], System.Globalization.CultureInfo.InvariantCulture);
            if (item is null)
            {
                return null;
            }

            return item.GetType().InvokeMember("ExtendedProperty", BindingFlags.InvokeMethod, null, item, [Property], System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            Release(item);
            Release(shellFolder);
            Release(shell);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
