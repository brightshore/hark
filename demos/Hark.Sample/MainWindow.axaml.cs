using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Hark.Avalonia;

namespace Hark.Sample;

public sealed partial class MainWindow : Window
{
    private INotificationManager? _notifications;
    private IBadgeManager? _badge;
    private WindowAttention? _attention;
    private NotificationHandle? _last;
    private int _shown;

    public MainWindow()
    {
        InitializeComponent();

        // Shipped next to the binaries (inside Contents/MacOS in the .app); an absolute path, which
        // every backend needs.
        var sampleImage = Path.Combine(AppContext.BaseDirectory, "Assets", "hark-sample.png");
        if (File.Exists(sampleImage))
            ImageBox.Text = sampleImage;

        Opened += async (_, _) =>
        {
            ConnectAttention();
            await ConnectBadgeAsync();
            await ConnectAsync();
        };
    }

    private async Task ConnectAsync()
    {
        try
        {
            _notifications = await AvaloniaNotifications.Manager;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Creating the manager threw";
            Write($"! {ex}");
            return;
        }

        if (!_notifications.IsSupported)
        {
            StatusText.Text = "Not supported here";
            CapabilitiesText.Text = _notifications.UnsupportedReason;
            Write($"unsupported: {_notifications.UnsupportedReason}");
            return;
        }

        StatusText.Text = "Connected";
        CapabilitiesText.Text = $"Capabilities: {_notifications.Capabilities}";
        Write($"connected, capabilities: {_notifications.Capabilities}");
        ShowButton.IsEnabled = BurstButton.IsEnabled = CloseButton.IsEnabled = true;

        // DispatchingNotificationManager delivers these on the UI thread.
        _notifications.Activated += (_, e) =>
        {
            Write($"activated  #{e.Handle.Id}  action={e.ActionId ?? "(body)"}  token={e.ActivationToken ?? "(none)"}");
            Activate();
        };
        _notifications.Closed += (_, e) => Write($"closed     #{e.Handle.Id}  reason={e.Reason}");
    }

    private async Task ConnectBadgeAsync()
    {
        _badge = await AvaloniaBadge.Manager;
        if (!_badge.IsSupported)
        {
            BadgeStatusText.Text = $"Badge: not supported here. {_badge.UnsupportedReason}";
            Write($"badge unsupported: {_badge.UnsupportedReason}");
            return;
        }

        BadgeStatusText.Text = $"Badge: {_badge.Capabilities}";
        Write($"badge, capabilities: {_badge.Capabilities}");
        SetCountButton.IsEnabled = _badge.Capabilities.HasFlag(BadgeCapabilities.Count);
        SetTextButton.IsEnabled = _badge.Capabilities.HasFlag(BadgeCapabilities.Text);
        SetProgressButton.IsEnabled = HideProgressButton.IsEnabled = _badge.Capabilities.HasFlag(BadgeCapabilities.Progress);
        ClearBadgeButton.IsEnabled = true;
    }

    // Made here, on the UI thread, once the window has its native handle.
    private void ConnectAttention()
    {
        _attention = new WindowAttention(this);
        if (!_attention.IsSupported)
        {
            AttentionStatusText.Text = $"Attention: not supported here. {_attention.UnsupportedReason}";
            Write($"attention unsupported: {_attention.UnsupportedReason}");
            return;
        }

        AttentionStatusText.Text = $"Attention: {_attention.Capabilities}";
        Write($"attention, capabilities: {_attention.Capabilities}");
        RequestAttentionButton.IsEnabled = CancelAttentionButton.IsEnabled = true;
    }

    private async void OnSetCount(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BadgeBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var count))
        {
            Write($"! '{BadgeBox.Text}' is not a count");
            return;
        }
        await BadgeAsync($"count={count}", badge => badge.SetCountAsync(count));
    }

    private async void OnSetText(object? sender, RoutedEventArgs e)
    {
        var text = BadgeBox.Text;
        await BadgeAsync($"text=\"{text}\"", badge => badge.SetTextAsync(text));
    }

    private async void OnSetProgress(object? sender, RoutedEventArgs e)
    {
        var progress = Math.Round(ProgressSlider.Value, 2);
        await BadgeAsync($"progress={progress:0.00}", badge => badge.SetProgressAsync(progress));
    }

    private async void OnHideProgress(object? sender, RoutedEventArgs e)
        => await BadgeAsync("progress hidden", badge => badge.SetProgressAsync(null));

    private async void OnClearBadge(object? sender, RoutedEventArgs e)
        => await BadgeAsync("cleared", badge => badge.ClearAsync());

    private async Task BadgeAsync(string what, Func<IBadgeManager, Task> call)
    {
        if (_badge is null)
            return;
        try
        {
            await call(_badge);
            Write($"badge      {what}");
        }
        catch (Exception ex)
        {
            Write($"! badge {what} failed: {ex.Message}");
        }
    }

    private async void OnRequestAttention(object? sender, RoutedEventArgs e)
    {
        if (_attention is null)
            return;
        var level = (AttentionLevel)Math.Max(0, AttentionLevelBox.SelectedIndex);
        Write($"attention  {level} in 3 s: switch to another app now");
        await Task.Delay(TimeSpan.FromSeconds(3));
        try
        {
            _attention.Request(level);
            Write($"attention  {level} requested");
        }
        catch (Exception ex)
        {
            Write($"! attention failed: {ex.Message}");
        }
    }

    private void OnCancelAttention(object? sender, RoutedEventArgs e)
    {
        if (_attention is null)
            return;
        try
        {
            _attention.Cancel();
            Write("attention  cancelled");
        }
        catch (Exception ex)
        {
            Write($"! cancel failed: {ex.Message}");
        }
    }

    private async void OnShow(object? sender, RoutedEventArgs e) => await ShowAsync(BuildNotification(TitleBox.Text));

    private async void OnBurst(object? sender, RoutedEventArgs e)
    {
        for (var i = 1; i <= 3; i++)
            await ShowAsync(BuildNotification($"{TitleBox.Text} ({i}/3)"));
    }

    private async void OnCloseLast(object? sender, RoutedEventArgs e)
    {
        if (_notifications is null || _last is null)
        {
            Write("nothing shown yet");
            return;
        }
        try
        {
            await _notifications.CloseAsync(_last);
            Write($"close      #{_last.Id} requested");
        }
        catch (Exception ex)
        {
            Write($"! close failed: {ex.Message}");
        }
    }

    private void OnClearLog(object? sender, RoutedEventArgs e) => Log.Items.Clear();

    private async Task ShowAsync(Notification? notification)
    {
        if (_notifications is null || notification is null)
            return;
        try
        {
            _last = await _notifications.ShowAsync(notification);
            Write($"shown      #{_last.Id}{(_last.Tag is null ? "" : $"  tag={_last.Tag}")}  \"{notification.Title}\"");
        }
        catch (Exception ex)
        {
            Write($"! show failed: {ex.Message}");
        }
    }

    private Notification? BuildNotification(string? title)
    {
        TimeSpan? expiration = null;
        if (NeverExpireBox.IsChecked == true)
            expiration = Timeout.InfiniteTimeSpan;
        else if (!string.IsNullOrWhiteSpace(ExpirationBox.Text))
        {
            if (!double.TryParse(ExpirationBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds))
            {
                Write($"! '{ExpirationBox.Text}' is not a number of seconds");
                return null;
            }
            expiration = TimeSpan.FromSeconds(seconds);
        }

        return new Notification
        {
            Title = $"{title} · {++_shown}",
            Body = BodyBox.Text,
            ImagePath = string.IsNullOrWhiteSpace(ImageBox.Text) ? null : ImageBox.Text.Trim(),
            Tag = string.IsNullOrWhiteSpace(TagBox.Text) ? null : TagBox.Text.Trim(),
            Urgency = (NotificationUrgency)Math.Max(0, UrgencyBox.SelectedIndex),
            Silent = SilentBox.IsChecked == true,
            Expiration = expiration,
            Actions = ParseActions(ActionsBox.Text),
        };
    }

    // "reply:Reply, mute:Mute" -> [reply/Reply, mute/Mute]. A bare word is both id and label.
    private static NotificationAction[] ParseActions(string? text) =>
        (text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', 2, StringSplitOptions.TrimEntries))
            .Select(pair => new NotificationAction(pair[0], pair.Length > 1 ? pair[1] : pair[0]))
            .ToArray();

    private void Write(string line)
    {
        var entry = $"{DateTime.Now:HH:mm:ss}  {line}";
        Log.Items.Add(entry);
        Log.ScrollIntoView(entry);
    }
}
