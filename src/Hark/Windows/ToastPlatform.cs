using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Hark.Windows;

/// <summary>
/// <see cref="IToastPlatform"/> over the real <c>Windows.UI.Notifications</c> toast API.
/// </summary>
/// <remarks>
/// <para>
/// An unpackaged app has no package identity, so Windows needs to be told who it is: the process
/// gets an explicit AppUserModelID, and that id is registered for the current user under
/// <c>HKCU\Software\Classes\AppUserModelId</c> with a display name and icon. That is the
/// registration the Windows App SDK writes for unpackaged apps; no Start menu shortcut and no admin
/// rights are needed.
/// </para>
/// <para>
/// Events arrive on each toast object, so the object is kept alive while the toast can still be
/// clicked: on screen, or in Notification Center after it times out. It is released when the
/// toast is activated, dismissed, hidden or replaced.
/// </para>
/// <para>
/// Clicks come through <see cref="ToastActivator"/> when it registers, which also covers clicks
/// after the app has exited; the toast's own Activated event is then not subscribed, so each
/// click arrives exactly once. Without the activator, that event is the fallback.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class ToastPlatform : IToastPlatform
{
    private const string Group = "hark";

    private static readonly Guid IID_IToastNotificationManagerStatics = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");
    private static readonly Guid IID_IToastNotificationManagerStatics2 = new("7ab93c52-0e48-4750-ba9d-1a4113981847");
    private static readonly Guid IID_IToastNotificationFactory = new("04124b20-82c6-4229-b109-fd9ed4662b53");
    private static readonly Guid IID_IToastNotification = new("997e2675-059e-4e60-8b06-1760917c8b80");
    private static readonly Guid IID_IToastNotification2 = new("9dfb9fd1-143a-490e-90bf-b9fba7132de7");
    private static readonly Guid IID_IToastActivatedEventArgs = new("e3bf92f3-c197-436f-8265-0625824f8dac");
    private static readonly Guid IID_IToastDismissedEventArgs = new("3f89d935-d9cb-4538-a0f0-ffe7659938f8");
    private static readonly Guid IID_IToastFailedEventArgs = new("35176862-cfd4-44f8-ad64-f500fd896c3b");
    private static readonly Guid IID_IXmlDocument = new("f7f3a506-1e87-42d6-bcfb-b8c809fa5494");
    private static readonly Guid IID_IXmlDocumentIO = new("6cd0e74e-ee65-4489-9ebf-ca43e87ba637");

    // Vtable slots, counted from IInspectable's six.
    private const int Statics_CreateToastNotifierWithId = WinRT.InspectableSlots + 1;
    private const int Statics2_get_History = WinRT.InspectableSlots + 0;
    private const int History_RemoveGroupedTagWithId = WinRT.InspectableSlots + 2;
    private const int Notifier_Show = WinRT.InspectableSlots + 0;
    private const int Notifier_Hide = WinRT.InspectableSlots + 1;
    private const int Notifier_get_Setting = WinRT.InspectableSlots + 2;
    private const int Factory_CreateToastNotification = WinRT.InspectableSlots + 0;
    private const int Toast_add_Dismissed = WinRT.InspectableSlots + 3;
    private const int Toast_add_Activated = WinRT.InspectableSlots + 5;
    private const int Toast_add_Failed = WinRT.InspectableSlots + 7;
    private const int Toast2_put_Tag = WinRT.InspectableSlots + 0;
    private const int Toast2_put_Group = WinRT.InspectableSlots + 2;
    private const int Toast2_put_SuppressPopup = WinRT.InspectableSlots + 4;
    private const int ActivatedArgs_get_Arguments = WinRT.InspectableSlots + 0;
    private const int DismissedArgs_get_Reason = WinRT.InspectableSlots + 0;
    private const int FailedArgs_get_ErrorCode = WinRT.InspectableSlots + 0;
    private const int XmlIO_LoadXml = WinRT.InspectableSlots + 0;

    private readonly string _appId;
    private readonly IntPtr _notifier;
    private readonly IntPtr _history;
    private readonly IntPtr _toastFactory;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Tracked> _toasts = new(StringComparer.Ordinal);

    /// <summary>A live toast and its COM identity (its IUnknown), which is what events are matched on.</summary>
    private readonly record struct Tracked(IntPtr Toast, IntPtr Identity);
    private IToastEventSink? _sink;
    private bool _activatorActive;

    /// <summary>Why clicks after exit will not reach the app, or null when the activator is registered.</summary>
    public string? ActivatorUnavailableReason { get; private set; }

    private ToastPlatform(string appId, IntPtr notifier, IntPtr history, IntPtr toastFactory)
    {
        _appId = appId;
        _notifier = notifier;
        _history = history;
        _toastFactory = toastFactory;
    }

    /// <summary>Returns the platform, or null with the reason it cannot show toasts.</summary>
    public static ToastPlatform? TryCreate(AppIdentity identity, out string? unsupportedReason)
    {
        unsupportedReason = null;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            unsupportedReason = "Toast notifications need Windows 10 version 1809 or later.";
            return null;
        }

        WinRT.EnsureComInitialized();
        IntPtr statics = 0, statics2 = 0, notifier = 0, history = 0, factory = 0;
        try
        {
            Register(identity);

            statics = WinRT.GetActivationFactory("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics);
            using (var appId = new WinRT.HString(identity.AppId))
            {
                WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)WinRT.Slot(statics, Statics_CreateToastNotifierWithId))(
                    statics, appId.Handle, &notifier), "CreateToastNotifierWithId");
            }

            // Advisory only. Windows answers 0x80070490 (element not found) here for an AUMID it has
            // no shortcut for, even where Show then works, so only a definite "disabled" is a refusal.
            // Anything else is left for Show to answer.
            int setting;
            var settingResult = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)WinRT.Slot(notifier, Notifier_get_Setting))(notifier, &setting);
            if (settingResult >= 0 && setting != 0)
            {
                unsupportedReason = setting switch
                {
                    1 => "Notifications are turned off for this app in Settings > System > Notifications.",
                    2 => "Notifications are turned off for this user in Settings > System > Notifications.",
                    3 => "Notifications are disabled by group policy.",
                    _ => $"Windows reports notifications as disabled (NotificationSetting {setting}).",
                };
                return null;
            }

            statics2 = WinRT.QueryInterface(statics, IID_IToastNotificationManagerStatics2);
            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)WinRT.Slot(statics2, Statics2_get_History))(statics2, &history), "ToastNotificationManager.History");

            factory = WinRT.GetActivationFactory("Windows.UI.Notifications.ToastNotification", IID_IToastNotificationFactory);

            var platform = new ToastPlatform(identity.AppId, notifier, history, factory);
            notifier = history = factory = 0; // owned by the platform now

            platform.ActivatorUnavailableReason = ToastActivator.Register(identity.AppId, platform.OnActivatorInvoked);
            platform._activatorActive = platform.ActivatorUnavailableReason is null;
            if (!platform._activatorActive)
                System.Diagnostics.Trace.TraceWarning($"Hark: toast clicks will only reach the app while it runs. {platform.ActivatorUnavailableReason}");
            return platform;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            unsupportedReason = $"Windows toast notifications are unavailable: {ex.Message}";
            return null;
        }
        finally
        {
            WinRT.Release(statics);
            WinRT.Release(statics2);
            WinRT.Release(notifier);
            WinRT.Release(history);
            WinRT.Release(factory);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    /// <summary>Gives the unpackaged process an identity Windows will attribute toasts to.</summary>
    private static void Register(AppIdentity identity)
    {
        WinRT.Check(SetCurrentProcessExplicitAppUserModelID(identity.AppId), "SetCurrentProcessExplicitAppUserModelID");

        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{identity.AppId}");
        key.SetValue("DisplayName", identity.DisplayName);
        if (identity.IconPath is { } icon)
            key.SetValue("IconUri", Path.GetFullPath(icon));
        else
            key.DeleteValue("IconUri", throwOnMissingValue: false);
    }

    public void SetSink(IToastEventSink sink) => Volatile.Write(ref _sink, sink);

    public void Show(ToastRequest request)
    {
        WinRT.EnsureComInitialized();
        IntPtr document = 0, documentIO = 0, xml = 0, toast = 0, toast2 = 0;
        try
        {
            document = WinRT.ActivateInstance("Windows.Data.Xml.Dom.XmlDocument");
            documentIO = WinRT.QueryInterface(document, IID_IXmlDocumentIO);
            using (var content = new WinRT.HString(request.Xml))
                WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRT.Slot(documentIO, XmlIO_LoadXml))(documentIO, content.Handle), "XmlDocument.LoadXml");
            xml = WinRT.QueryInterface(document, IID_IXmlDocument);

            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)WinRT.Slot(_toastFactory, Factory_CreateToastNotification))(
                _toastFactory, xml, &toast), "CreateToastNotification");

            toast2 = WinRT.QueryInterface(toast, IID_IToastNotification2);
            using (var tag = new WinRT.HString(request.Tag))
                WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRT.Slot(toast2, Toast2_put_Tag))(toast2, tag.Handle), "ToastNotification.Tag");
            using (var group = new WinRT.HString(Group))
                WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRT.Slot(toast2, Toast2_put_Group))(toast2, group.Handle), "ToastNotification.Group");
            if (request.SuppressPopup)
                WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)WinRT.Slot(toast2, Toast2_put_SuppressPopup))(toast2, 1), "ToastNotification.SuppressPopup");

            var toastTag = request.Tag;
            if (!_activatorActive)
                Subscribe(toast, Toast_add_Activated, ToastEventIids.Activated, (sender, args) => OnActivated(toastTag, sender, args));
            Subscribe(toast, Toast_add_Dismissed, ToastEventIids.Dismissed, (sender, args) => OnDismissed(toastTag, sender, args));
            Subscribe(toast, Toast_add_Failed, ToastEventIids.Failed, (sender, args) => OnFailed(toastTag, sender, args));

            // Track before showing, so an event fired during Show finds it. The dictionary owns the
            // reference from here on; the identity is only compared, never dereferenced.
            var tracked = new Tracked(toast, IdentityOf(toast));
            toast = 0;
            Tracked replaced;
            lock (_gate)
            {
                _toasts.Remove(toastTag, out replaced);
                _toasts[toastTag] = tracked;
            }
            WinRT.Release(replaced.Toast);

            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRT.Slot(_notifier, Notifier_Show))(_notifier, tracked.Toast);
            if (hr < 0)
            {
                ForgetIfCurrent(toastTag, tracked.Identity);
                WinRT.Check(hr, "ToastNotifier.Show");
            }
        }
        finally
        {
            WinRT.Release(document);
            WinRT.Release(documentIO);
            WinRT.Release(xml);
            WinRT.Release(toast);
            WinRT.Release(toast2);
        }
    }

    public void Hide(string tag)
    {
        WinRT.EnsureComInitialized();
        Tracked tracked;
        lock (_gate)
            _toasts.Remove(tag, out tracked);
        if (tracked.Toast != 0)
        {
            // Fails harmlessly for a toast already gone from screen; History covers the rest.
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRT.Slot(_notifier, Notifier_Hide))(_notifier, tracked.Toast);
            WinRT.Release(tracked.Toast);
        }

        using var hTag = new WinRT.HString(tag);
        using var hGroup = new WinRT.HString(Group);
        using var hAppId = new WinRT.HString(_appId);
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, int>)WinRT.Slot(_history, History_RemoveGroupedTagWithId))(
            _history, hTag.Handle, hGroup.Handle, hAppId.Handle);
    }

    private static void Subscribe(IntPtr toast, int addSlot, Guid handlerIid, Action<IntPtr, IntPtr> callback)
    {
        var handler = ComEventHandler.Create(handlerIid, callback);
        try
        {
            long token;
            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)WinRT.Slot(toast, addSlot))(toast, handler, &token), "ToastNotification event subscription");
        }
        finally
        {
            // The toast holds its own reference from here; the handler goes when the toast does.
            WinRT.Release(handler);
        }
    }

    private void OnActivated(string tag, IntPtr sender, IntPtr args)
    {
        if (!IsCurrent(tag, sender))
            return;
        var arguments = "";
        if (args != 0)
        {
            var activated = WinRT.QueryInterface(args, IID_IToastActivatedEventArgs);
            try
            {
                IntPtr hstring;
                WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)WinRT.Slot(activated, ActivatedArgs_get_Arguments))(activated, &hstring), "ToastActivatedEventArgs.Arguments");
                arguments = WinRT.TakeString(hstring);
            }
            finally
            {
                WinRT.Release(activated);
            }
        }
        ForgetIfCurrent(tag, IdentityOf(sender));
        Volatile.Read(ref _sink)?.OnActivated(arguments, fromActivator: false);
    }

    /// <summary>A click delivered by the COM activator, on its apartment thread.</summary>
    private void OnActivatorInvoked(string arguments)
    {
        if (ToastArguments.Parse(arguments) is { } parsed)
        {
            Tracked tracked;
            lock (_gate)
                _toasts.Remove(parsed.ToastTag, out tracked);
            WinRT.Release(tracked.Toast);
        }
        Volatile.Read(ref _sink)?.OnActivated(arguments, fromActivator: true);
    }

    private void OnDismissed(string tag, IntPtr sender, IntPtr args)
    {
        // A toast replaced by a newer one with the same tag reports its own dismissal; that is
        // not the end of the notification the tag now stands for.
        if (!IsCurrent(tag, sender))
            return;
        var dismissed = WinRT.QueryInterface(args, IID_IToastDismissedEventArgs);
        int reason;
        try
        {
            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)WinRT.Slot(dismissed, DismissedArgs_get_Reason))(dismissed, &reason), "ToastDismissedEventArgs.Reason");
        }
        finally
        {
            WinRT.Release(dismissed);
        }
        // Timed out (2) means moved to Notification Center, where it can still be clicked: keep it.
        if (reason != 2)
            ForgetIfCurrent(tag, IdentityOf(sender));
        Volatile.Read(ref _sink)?.OnDismissed(tag, reason);
    }

    private void OnFailed(string tag, IntPtr sender, IntPtr args)
    {
        if (!IsCurrent(tag, sender))
            return;
        var failed = WinRT.QueryInterface(args, IID_IToastFailedEventArgs);
        int errorCode;
        try
        {
            WinRT.Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)WinRT.Slot(failed, FailedArgs_get_ErrorCode))(failed, &errorCode), "ToastFailedEventArgs.ErrorCode");
        }
        finally
        {
            WinRT.Release(failed);
        }
        ForgetIfCurrent(tag, IdentityOf(sender));
        Volatile.Read(ref _sink)?.OnFailed(tag, errorCode);
    }

    /// <summary>
    /// COM identity: the IUnknown pointer, the same for every interface of one object. Released at
    /// once; the value is only ever compared.
    /// </summary>
    private static IntPtr IdentityOf(IntPtr obj)
    {
        var unknown = WinRT.QueryInterface(obj, WinRT.IID_IUnknown);
        WinRT.Release(unknown);
        return unknown;
    }

    private bool IsCurrent(string tag, IntPtr sender)
    {
        var identity = IdentityOf(sender);
        lock (_gate)
            return _toasts.TryGetValue(tag, out var tracked) && tracked.Identity == identity;
    }

    /// <summary>Drops the tracked toast for <paramref name="tag"/>, only if it is still that object.</summary>
    private void ForgetIfCurrent(string tag, IntPtr identity)
    {
        Tracked tracked;
        lock (_gate)
        {
            if (!_toasts.TryGetValue(tag, out tracked) || tracked.Identity != identity)
                return;
            _toasts.Remove(tag);
        }
        WinRT.Release(tracked.Toast);
    }
}
