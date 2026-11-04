using System.Windows.Forms;
using XivConfigSync.Core;

namespace XivConfigSync.App;

/// <summary>The icon in the notification area (FR-004) with its menu and balloon notifications.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu;

    public TrayIcon(Action open, Action backupNow, Action exit)
    {
        menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Tray_Open, null, (_, _) => open());
        menu.Items.Add(Strings.Tray_BackupNow, null, (_, _) => backupNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Exit, null, (_, _) => exit());

        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/XIVConfigSync.ico")).Stream;
        icon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = Strings.App_Title,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => open();
    }

    /// <summary>Shows a balloon notification.</summary>
    public void Show(string title, string text) => icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    /// <summary>Sets the tooltip, at most 63 characters as Windows allows.</summary>
    public void SetText(string text) => icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        icon.Visible = false;
        icon.Icon?.Dispose();
        icon.Dispose();
        menu.Dispose();
    }
}
