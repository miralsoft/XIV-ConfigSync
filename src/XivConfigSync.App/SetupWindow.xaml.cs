using System.Windows;

namespace XivConfigSync.App;

/// <summary>The first-start wizard. All behaviour lives in <see cref="Core.ViewModels.SetupViewModel"/>.</summary>
public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>Whether the set-up was completed, so closing the window does not end the application.</summary>
    public bool Completed { get; set; }
}
