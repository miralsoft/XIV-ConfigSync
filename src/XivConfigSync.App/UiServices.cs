using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using XivConfigSync.Core;
using XivConfigSync.Core.ViewModels;

namespace XivConfigSync.App;

/// <summary>Dialogs, notifications and the UI thread for the view models.</summary>
internal sealed class UiServices : IUiServices
{
    private readonly Func<Window?> owner;
    private readonly Action<string, string> notify;

    public UiServices(Func<Window?> owner, Action<string, string> notify)
    {
        this.owner = owner;
        this.notify = notify;
    }

    public string? PickFolder(string title, string? initial)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initial))
        {
            var start = initial;
            while (!string.IsNullOrEmpty(start) && !System.IO.Directory.Exists(start))
            {
                start = System.IO.Path.GetDirectoryName(start);
            }

            if (!string.IsNullOrEmpty(start))
            {
                dialog.InitialDirectory = start;
            }
        }

        return dialog.ShowDialog(owner()) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool? Ask(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public void Inform(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message) => Show(message, Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Notify(string title, string message) => notify(title, message);

    public void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    public void OpenFolder(string path)
    {
        if (System.IO.Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
    }

    private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var window = owner();
        return window is { IsVisible: true }
            ? MessageBox.Show(window, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
    }
}
