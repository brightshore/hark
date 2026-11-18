namespace Hark;

/// <summary>What the platform's app-icon badge can show.</summary>
[Flags]
public enum BadgeCapabilities
{
    /// <summary>Nothing: there is no badge here.</summary>
    None = 0,
    /// <summary>A count, through <see cref="IBadgeManager.SetCountAsync"/>.</summary>
    Count = 1 << 0,
    /// <summary>A short label of any text, through <see cref="IBadgeManager.SetTextAsync"/>.</summary>
    Text = 1 << 1,
    /// <summary>A progress bar on the icon, through <see cref="IBadgeManager.SetProgressAsync"/>.</summary>
    Progress = 1 << 2,
}
