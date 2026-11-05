using System.Windows;

namespace XivConfigSync.App;

/// <summary>The first-start wizard. All behaviour lives in <see cref="Core.ViewModels.SetupViewModel"/>.</summary>
public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            // Not while the set-up writes into the sync folder (review 3, finding 3).
            if (!Completed && DataContext is Core.ViewModels.SetupViewModel { Working: true })
            {
                e.Cancel = true;
            }
        };
    }

    /// <summary>Whether the set-up was completed, so closing the window does not end the application.</summary>
    public bool Completed { get; set; }
}
