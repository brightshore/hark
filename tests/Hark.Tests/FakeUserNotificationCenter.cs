using Hark.MacOS;

namespace Hark.Tests;

/// <summary>Records what the macOS manager asks of the OS and lets a test play the user's responses.</summary>
internal sealed class FakeUserNotificationCenter : IUserNotificationCenter
{
    public (bool Granted, string? Error) Authorization { get; set; } = (true, null);
    public List<MacNotificationRequest> Added { get; } = [];
    public List<IReadOnlyCollection<MacCategory>> CategoryRegistrations { get; } = [];
    public List<string> Removed { get; } = [];
    public IMacResponseSink? Sink { get; private set; }

    public Task<(bool Granted, string? Error)> RequestAuthorizationAsync() => Task.FromResult(Authorization);

    public void SetCategories(IReadOnlyCollection<MacCategory> categories) => CategoryRegistrations.Add(categories);

    public Task AddAsync(MacNotificationRequest request)
    {
        Added.Add(request);
        return Task.CompletedTask;
    }

    public void Remove(string identifier) => Removed.Add(identifier);

    public void SetResponseSink(IMacResponseSink sink) => Sink = sink;
}
