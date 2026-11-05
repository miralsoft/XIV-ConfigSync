using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using XivConfigSync.Core;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Logging;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Settings;
using XivConfigSync.Core.Store;
using XivConfigSync.Core.ViewModels;
using XivConfigSync.Platform;

namespace XivConfigSync.App;

/// <summary>Start-up, the single instance, the exception handlers, the tray and the windows.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF owns the Application object; its fields are disposed in OnExit, the end of its lifetime.")]
public partial class App : Application
{
    private SingleInstance? instance;
    private Log log = Log.None;
    private LocalData? local;
    private SyncEngine? engine;
    private GameMonitor? game;
    private TrayIcon? tray;
    private MainWindow? mainWindow;
    private MainViewModel? mainModel;
    private UiServices? ui;
    private bool exiting;
    private bool toldAboutTray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            // A failure before a window or the tray exists would leave an invisible process holding
            // the single-instance lock (review 3, finding 5).
            log.Error("start", "Start failed: " + ex);
            MessageBox.Show(Strings.Error_Unexpected, Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Start(StartupEventArgs e)
    {
        var syncFileArg = e.Args.FirstOrDefault(a => a.EndsWith(SyncStore.SyncFileExtension, StringComparison.OrdinalIgnoreCase));
        var startInTray = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);

        instance = new SingleInstance("XIVConfigSync");
        if (!instance.IsFirst)
        {
            instance.SendToFirst(syncFileArg ?? "show");
            Shutdown(0);
            return;
        }

        local = new LocalData(DataRoot());
        log = new Log(local.LogsFolder);
        RegisterExceptionHandlers();
        AppSettings settings;
        try
        {
            settings = AppSettings.Load(local.SettingsPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            log.Error("start", "Settings could not be read: " + ex.Message);
            MessageBox.Show(Text.Format(Strings.Error_Settings, local.SettingsPath), Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ApplyLanguage(settings.Language);
        log.Info("start", $"XIV ConfigSync {SyncEngine.AppVersion} started.");

        RuleSet rules;
        try
        {
            rules = RuleSet.Load(local.RulesPath);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            log.Warning("start", "Rules could not be read, defaults are used: " + ex.Message);
            rules = new RuleSet(new RuleFile());
        }

        game = new GameMonitor();
        engine = new SyncEngine(local, settings, rules, game, new CloudStateReader(), log);
        ui = new UiServices(() => (Window?)mainWindow ?? Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), (title, text) => tray?.Show(title, text));
        _ = instance.ListenAsync(message => Dispatcher.BeginInvoke(() => OnSecondInstance(message)));

        // A device that is set up always starts normally, also when its sync folder is not reachable
        // right now (a drive not connected, OneDrive not started yet): the overview says so and the
        // application tries again; it never runs the set-up on its own (review 3, finding 6).
        if (!settings.IsSetUp)
        {
            ShowSetup(syncFileArg, changing: false);
        }
        else
        {
            ShowMain(startInTray && syncFileArg is null);
            if (syncFileArg is not null)
            {
                mainModel!.OpenSyncFile(syncFileArg);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        mainModel?.Dispose();
        tray?.Dispose();
        game?.Dispose();
        instance?.Dispose();
        log.Info("exit", "XIV ConfigSync ended.");
        base.OnExit(e);
    }

    private static string DataRoot()
    {
#if DEBUG
        // Development only: a separate data folder for manual tests. Absent from a release build (C-04).
        var overrideRoot = Environment.GetEnvironmentVariable("XIVCS_DEV_DATA");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot;
        }
#endif
        return LocalData.DefaultRoot;
    }

    private static void ApplyLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("task", "Unobserved task exception: " + args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => log.Error("domain", "Unhandled exception: " + args.ExceptionObject);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        // The safety net (WPF profile): log the detail, show a general message, stay open.
        log.Error("ui", "Unexpected error: " + args.Exception);
        args.Handled = true;
        MessageBox.Show(Strings.Error_Unexpected, Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Error);
        if (tray is null && !Windows.OfType<Window>().Any(w => w.IsVisible))
        {
            // Nothing left the user could reach: end rather than run on invisibly.
            Shutdown(1);
        }
    }

    private void ShowSetup(string? syncFileArg, bool changing)
    {
        var shell = new ShellIntegration(Environment.ProcessPath!, Strings.SyncFile_TypeName);
        var model = new SetupViewModel(engine!, ui!, shell, new GameLocator(), syncFileArg);
        var window = new SetupWindow { DataContext = model };
        model.Completed += (_, _) =>
        {
            if (window.Completed)
            {
                return;
            }

            window.Completed = true;
            window.Close();
            ShowMain(startHidden: false);
        };
        window.Closed += (_, _) =>
        {
            if (window.Completed)
            {
                return;
            }

            if (changing)
            {
                // Changing the sync folder was cancelled: back to the folder in use.
                ShowMain(startHidden: false);
            }
            else
            {
                Shutdown(0);
            }
        };
        window.Show();
    }

    private void ShowMain(bool startHidden)
    {
        // One overview and one tray icon per process: a second call only brings the window forward.
        if (mainWindow is not null)
        {
            ShowMainWindow();
            return;
        }

        var shell = new ShellIntegration(Environment.ProcessPath!, Strings.SyncFile_TypeName);
        mainModel = new MainViewModel(engine!, ui!, log, game!, shell, new GameLocator());
        mainModel.LogPage.Folder = local!.LogsFolder;
        mainWindow = new MainWindow { DataContext = mainModel };
        mainWindow.Closing += OnMainClosing;
        mainModel.SetupRequested += (_, _) => RestartSetup();
        tray = new TrayIcon(ShowMainWindow, () => _ = mainModel.BackupNowAsync(), ExitApplication);
        mainModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusText) && tray is not null && mainModel is not null)
            {
                tray.SetText(Strings.App_Title + ": " + mainModel.StatusText);
            }
        };

        if (startHidden)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = false;
            mainWindow.Show();
            mainWindow.Hide();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.ShowInTaskbar = true;
        }
        else
        {
            mainWindow.Show();
        }

        _ = mainModel.StartAsync();
    }

    private void RestartSetup()
    {
        exiting = true;
        mainWindow?.Close();
        mainWindow = null;
        mainModel?.Dispose();
        mainModel = null;
        tray?.Dispose();
        tray = null;
        exiting = false;
        ShowSetup(null, changing: true);
    }

    private void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    private void OnSecondInstance(string message)
    {
        if (mainModel is null)
        {
            // Still in the set-up: bring its window forward.
            Windows.OfType<Window>().FirstOrDefault()?.Activate();
            return;
        }

        ShowMainWindow();
        if (message.EndsWith(SyncStore.SyncFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            mainModel.OpenSyncFile(message);
        }
    }

    private void OnMainClosing(object? sender, CancelEventArgs e)
    {
        if (exiting)
        {
            return;
        }

        var settings = engine!.Settings;
        if (settings.CloseAction is null)
        {
            var minimize = MessageBox.Show(mainWindow!, Strings.Close_AskText, Strings.Close_AskTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (minimize == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            settings.CloseAction = minimize == MessageBoxResult.Yes ? CloseAction.MinimizeToTray : CloseAction.Exit;
            engine.SaveSettings();
        }

        if (settings.CloseAction == CloseAction.MinimizeToTray)
        {
            e.Cancel = true;
            mainWindow!.Hide();
            if (!toldAboutTray)
            {
                // Windows 11 hides new tray icons in the overflow; say once where the application is.
                toldAboutTray = true;
                tray?.Show(Strings.App_Title, Strings.Tray_StillRunning);
            }

            return;
        }

        if (mainModel?.Busy == true)
        {
            e.Cancel = true;
            MessageBox.Show(mainWindow!, Strings.Close_Busy, Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        exiting = true;
        Shutdown(0);
    }

    private void ExitApplication()
    {
        if (mainModel?.Busy == true)
        {
            MessageBox.Show(Strings.Close_Busy, Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        exiting = true;
        mainWindow?.Close();
        Shutdown(0);
    }
}
