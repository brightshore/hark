namespace Hark.Linux;

/// <summary>
/// The <c>com.canonical.Unity.LauncherEntry</c> wire surface, and nothing else.
/// <see cref="LauncherEntryBadgeManager"/> talks only to this, so its decisions are testable without a
/// bus. <see cref="DBusLauncherEntryBus"/> is the one real implementation.
/// </summary>
internal interface ILauncherEntryBus : IAsyncDisposable
{
    /// <summary>Emits the <c>Update(sa{sv})</c> signal for <paramref name="appUri"/>, carrying only the properties that are set.</summary>
    void Update(string appUri, LauncherEntryUpdate update);
}

/// <summary>The LauncherEntry properties Hark sends. A null member is left out of the <c>a{sv}</c>, so the dock keeps its value.</summary>
internal sealed record LauncherEntryUpdate(long? Count = null, bool? CountVisible = null, double? Progress = null, bool? ProgressVisible = null);
