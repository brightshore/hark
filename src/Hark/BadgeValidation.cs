namespace Hark;

/// <summary>The argument and capability checks every badge backend makes, in one place.</summary>
internal static class BadgeValidation
{
    public static void RequireCapability(BadgeCapabilities capabilities, BadgeCapabilities needed)
    {
        if (!capabilities.HasFlag(needed))
            throw new NotSupportedException($"The app badge here cannot show {needed}: it supports {capabilities}. Check IBadgeManager.Capabilities first.");
    }

    public static void ValidateCount(int count) => ArgumentOutOfRangeException.ThrowIfNegative(count);

    public static void ValidateProgress(double? progress)
    {
        // NaN fails both comparisons, so it is refused with the rest.
        if (progress is { } value && !(value >= 0 && value <= 1))
            throw new ArgumentOutOfRangeException(nameof(progress), value, "Progress runs from 0 to 1; null hides it.");
    }
}
