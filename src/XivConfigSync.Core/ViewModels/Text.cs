using System.Globalization;
using XivConfigSync.Core.Engine;

namespace XivConfigSync.Core.ViewModels;

/// <summary>Looks up translated texts by key, for keys that are data (reasons, refusals, matrix notes).</summary>
public static class Text
{
    /// <summary>The text for a key, or the key itself when it is missing.</summary>
    public static string Get(string key) => Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>A formatted text.</summary>
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>The message for an exception: a refusal's own text, otherwise the exception's message.</summary>
    public static string ForError(Exception ex) =>
        ex is RefusedException refused ? Get(refused.ReasonKey) : Format(Strings.Error_Generic, ex.Message);

    /// <summary>A time in the user's culture, local time.</summary>
    public static string Time(DateTimeOffset? time) =>
        time is null ? Strings.Common_Never : time.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
