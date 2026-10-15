using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Hark.Avalonia;

namespace Hark.Sample;

public sealed partial class MainWindow : Window
{
    private INotificationManager? _notifications;
    private NotificationHandle? _last;
    private int _shown;

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await ConnectAsync();
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
