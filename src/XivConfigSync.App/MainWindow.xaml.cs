using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using XivConfigSync.Core.ViewModels;

namespace XivConfigSync.App;

/// <summary>The main window. All behaviour lives in <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged model)
        {
            model.PropertyChanged += OnModelPropertyChanged;
        }
    }

    /// <summary>
    /// WPF announces a live region only when its automation peer raises the event, so the status
    /// line does it here whenever the status changes (review 3, finding 12).
    /// </summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.StatusText) || !IsVisible)
        {
            return;
        }

        var peer = UIElementAutomationPeer.FromElement(StatusLine) ?? UIElementAutomationPeer.CreatePeerForElement(StatusLine);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
