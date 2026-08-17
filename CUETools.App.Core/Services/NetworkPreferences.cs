using System.Threading.Tasks;

namespace CUETools.Wpf.Services;

/// <summary>
/// The things CUETools can do on its own that reach the network, and whether the user has
/// said yes to them.
///
/// The rule is the one D-069 set for database submission: ask once, remember the answer, and
/// do nothing before an answer. Silently defaulting a convenience off is not better than
/// silently defaulting it on - the user ends up with a feature that appears broken and no
/// idea a choice exists. Asking once, at a moment the user can see, is what respects both.
///
/// Answering is a one-time event per profile. Changing the answer later belongs to Settings.
/// </summary>
public static class NetworkPreferences
{
    public const string ArtworkPromptTitle = "Look up cover art automatically?";

    public const string ArtworkPromptMessage =
        "When you insert a disc, CUETools can look up its cover art and show it, " +
        "so a rip has artwork without you asking for it. Doing that contacts the " +
        "CUETools Database and the Cover Art Archive.\n\n" +
        "Choose OK to let it look up cover art automatically, or Cancel to keep it " +
        "manual. Either way, nothing is uploaded, and you can change this later in " +
        "Settings.";

    /// <summary>True when the user has not yet been asked about automatic artwork.</summary>
    public static bool NeedsArtworkAnswer(AppSettings settings)
        => settings != null && !settings.AutoFetchArtAnswered;

    /// <summary>
    /// Asks once and records the answer. Returns the answer, which is also what
    /// <see cref="AppSettings.AutoFetchArtOnDiscRead"/> then reports. A head with no prompt
    /// cannot ask, so it records nothing and leaves the feature off: an unanswered question
    /// must not become a yes.
    /// </summary>
    public static async Task<bool> AskAboutArtworkAsync(AppSettings settings, IUserPrompt? prompt)
    {
        if (settings == null) return false;
        if (!NeedsArtworkAnswer(settings)) return settings.AutoFetchArtOnDiscRead;
        if (prompt == null) return false;

        bool yes = await prompt.ConfirmOkCancelAsync(ArtworkPromptMessage, ArtworkPromptTitle);
        settings.AutoFetchArtOnDiscRead = yes;
        settings.AutoFetchArtAnswered = true;
        return yes;
    }
}
