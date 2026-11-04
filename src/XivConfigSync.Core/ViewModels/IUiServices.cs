namespace XivConfigSync.Core.ViewModels;

/// <summary>What the view models need from the user interface; implemented in the WPF project.</summary>
public interface IUiServices
{
    /// <summary>Lets the user choose a folder; null when cancelled.</summary>
    string? PickFolder(string title, string? initial);

    /// <summary>Asks a yes/no question.</summary>
    bool Confirm(string title, string message);

    /// <summary>Asks a question with yes, no and cancel; null means cancel.</summary>
    bool? Ask(string title, string message);

    /// <summary>Shows a message.</summary>
    void Inform(string title, string message);

    /// <summary>Shows an error message.</summary>
    void ShowError(string message);

    /// <summary>A notification in the tray, if the window is not visible.</summary>
    void Notify(string title, string message);

    /// <summary>Runs an action on the UI thread.</summary>
    void OnUiThread(Action action);

    /// <summary>Opens a folder in Explorer.</summary>
    void OpenFolder(string path);
}
