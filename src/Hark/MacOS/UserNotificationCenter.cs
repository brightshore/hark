using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.MacOS;

/// <summary>
/// <see cref="IUserNotificationCenter"/> over the real <c>UNUserNotificationCenter</c>, through the
/// Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class UserNotificationCenter : IUserNotificationCenter
{
    private const string FrameworkPath = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";

    // UNAuthorizationOptions
    private const nuint AuthorizationBadge = 1 << 0;
    private const nuint AuthorizationSound = 1 << 1;
    private const nuint AuthorizationAlert = 1 << 2;

    // UNNotificationPresentationOptions
    private const nuint PresentSound = 1 << 1;
    private const nuint PresentList = 1 << 3;
    private const nuint PresentBanner = 1 << 4;

    // UNNotificationActionOptionForeground: a button brings the app forward, like a body click.
    private const nuint ActionForeground = 1 << 2;

    // UNNotificationCategoryOptionCustomDismissAction: without it, a dismissal is never reported.
    private const nuint CategoryCustomDismissAction = 1 << 0;

    // The OS holds one delegate per process, and so does Hark: the last center created receives.
    private static IMacResponseSink? s_sink;
    private static IntPtr s_delegate;
    private static readonly Lock s_delegateGate = new();

    private readonly IntPtr _center;

    private UserNotificationCenter(IntPtr center) => _center = center;

    /// <summary>
    /// Returns the center, or null with the reason it cannot exist. <c>currentNotificationCenter</c>
    /// raises an Objective-C exception, which would kill the process, unless the app runs from a
    /// bundle, so that is checked first.
    /// </summary>
    public static UserNotificationCenter? TryCreate(out string? unsupportedReason)
    {
        string? reason = null;
        var center = ObjC.InPool(() =>
        {
            var bundle = ObjC.Send(ObjC.GetClass("NSBundle"), ObjC.Selector("mainBundle"));
            var identifier = ObjC.ToManagedString(ObjC.Send(bundle, ObjC.Selector("bundleIdentifier")));
            var path = ObjC.ToManagedString(ObjC.Send(bundle, ObjC.Selector("bundlePath")));
            if (string.IsNullOrEmpty(identifier) || path is null || !path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                reason = "macOS only delivers notifications to an app running from a .app bundle with a "
                         + $"CFBundleIdentifier. This process runs from '{path}'.";
                return IntPtr.Zero;
            }

            if (!NativeLibrary.TryLoad(FrameworkPath, out _))
            {
                reason = "The UserNotifications framework could not be loaded.";
                return IntPtr.Zero;
            }

            var centerClass = ObjC.GetClass("UNUserNotificationCenter");
            return ObjC.Retain(ObjC.Send(centerClass, ObjC.Selector("currentNotificationCenter")));
        });

        unsupportedReason = reason;
        if (center == IntPtr.Zero)
            return null;

        InstallDelegate(center);
        return new UserNotificationCenter(center);
    }

    public void SetResponseSink(IMacResponseSink sink) => Volatile.Write(ref s_sink, sink);

    public Task<(bool Granted, string? Error)> RequestAuthorizationAsync()
    {
        var completion = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, byte, IntPtr, void>)&OnAuthorization, completion);
        try
        {
            ObjC.SendVoid(_center, ObjC.Selector("requestAuthorizationWithOptions:completionHandler:"),
                AuthorizationAlert | AuthorizationSound | AuthorizationBadge, block);
        }
        finally
        {
            ObjCBlock.Release(block);
        }
        return completion.Task;
    }

    public void SetCategories(IReadOnlyCollection<MacCategory> categories) => ObjC.InPool(() =>
    {
        var actionClass = ObjC.GetClass("UNNotificationAction");
        var categoryClass = ObjC.GetClass("UNNotificationCategory");
        var nativeCategories = new IntPtr[categories.Count];
        var i = 0;
        foreach (var category in categories)
        {
            var actions = new IntPtr[category.Actions.Count];
            for (var a = 0; a < actions.Length; a++)
            {
                actions[a] = ObjC.Send(actionClass, ObjC.Selector("actionWithIdentifier:title:options:"),
                    ObjC.NSString(category.Actions[a].Id), ObjC.NSString(category.Actions[a].Label), ActionForeground);
            }
            nativeCategories[i++] = ObjC.Send(categoryClass, ObjC.Selector("categoryWithIdentifier:actions:intentIdentifiers:options:"),
                ObjC.NSString(category.Identifier), ObjC.NSArray(actions), ObjC.NSArray([]), CategoryCustomDismissAction);
        }
        ObjC.SendVoid(_center, ObjC.Selector("setNotificationCategories:"), ObjC.NSSet(nativeCategories));
    });

    public Task AddAsync(MacNotificationRequest request)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ObjC.InPool(() =>
        {
            var content = ObjC.Send(ObjC.Send(ObjC.GetClass("UNMutableNotificationContent"), ObjC.Selector("alloc")), ObjC.Selector("init"));
            try
            {
                ObjC.SendVoid(content, ObjC.Selector("setTitle:"), ObjC.NSString(request.Title));
                ObjC.SendVoid(content, ObjC.Selector("setBody:"), ObjC.NSString(request.Body));
                ObjC.SendVoid(content, ObjC.Selector("setCategoryIdentifier:"), ObjC.NSString(request.CategoryIdentifier));
                if (request.PlaySound)
                    ObjC.SendVoid(content, ObjC.Selector("setSound:"), ObjC.Send(ObjC.GetClass("UNNotificationSound"), ObjC.Selector("defaultSound")));
                if (ObjC.RespondsTo(content, "setInterruptionLevel:"))
                    ObjC.SendVoid(content, ObjC.Selector("setInterruptionLevel:"), (nuint)request.InterruptionLevel);
                if (request.ImagePath is { } imagePath && CreateAttachment(imagePath) is var attachment && attachment != IntPtr.Zero)
                    ObjC.SendVoid(content, ObjC.Selector("setAttachments:"), ObjC.NSArray([attachment]));

                var nativeRequest = ObjC.Send(ObjC.GetClass("UNNotificationRequest"), ObjC.Selector("requestWithIdentifier:content:trigger:"),
                    ObjC.NSString(request.Identifier), content, IntPtr.Zero);

                var block = ObjCBlock.Create((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnAdded, completion);
                try
                {
                    ObjC.SendVoid(_center, ObjC.Selector("addNotificationRequest:withCompletionHandler:"), nativeRequest, block);
                }
                finally
                {
                    ObjCBlock.Release(block);
                }
            }
            finally
            {
                ObjC.Release(content);
            }
        });
        return completion.Task;
    }

    public void Remove(string identifier) => ObjC.InPool(() =>
    {
        var identifiers = ObjC.NSArray([ObjC.NSString(identifier)]);
        ObjC.SendVoid(_center, ObjC.Selector("removeDeliveredNotificationsWithIdentifiers:"), identifiers);
        ObjC.SendVoid(_center, ObjC.Selector("removePendingNotificationRequestsWithIdentifiers:"), identifiers);
    });

    /// <summary>
    /// The OS moves an attachment's file into its own store, so it gets a copy, never the caller's
    /// file. An image that cannot be attached is dropped: the notification still goes out.
    /// </summary>
    private static IntPtr CreateAttachment(string imagePath)
    {
        if (!File.Exists(imagePath))
            return IntPtr.Zero;
        var copy = Path.Combine(Path.GetTempPath(), $"hark-{Guid.NewGuid():N}{Path.GetExtension(imagePath)}");
        try
        {
            File.Copy(imagePath, copy);
        }
        catch (IOException)
        {
            return IntPtr.Zero;
        }
        catch (UnauthorizedAccessException)
        {
            return IntPtr.Zero;
        }

        var url = ObjC.Send(ObjC.GetClass("NSURL"), ObjC.Selector("fileURLWithPath:"), ObjC.NSString(copy));
        IntPtr error = IntPtr.Zero;
        var attachment = ObjC.Send(ObjC.GetClass("UNNotificationAttachment"), ObjC.Selector("attachmentWithIdentifier:URL:options:error:"),
            ObjC.NSString("image"), url, IntPtr.Zero, &error);
        if (attachment == IntPtr.Zero)
            File.Delete(copy);
        return attachment;
    }

    private static void InstallDelegate(IntPtr center)
    {
        lock (s_delegateGate)
        {
            if (s_delegate == IntPtr.Zero)
            {
                var cls = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), "HarkUserNotificationCenterDelegate");
                // v@:@@@? : void return, self, _cmd, center, notification/response, completion block.
                ObjC.AddMethod(cls, ObjC.Selector("userNotificationCenter:willPresentNotification:withCompletionHandler:"),
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&WillPresent, "v@:@@@?");
                ObjC.AddMethod(cls, ObjC.Selector("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"),
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidReceiveResponse, "v@:@@@?");
                var protocol = ObjC.GetProtocol("UNUserNotificationCenterDelegate");
                if (protocol != IntPtr.Zero)
                    ObjC.AddProtocol(cls, protocol);
                ObjC.RegisterClassPair(cls);

                // alloc/init's reference is never released: the center holds its delegate weakly.
                s_delegate = ObjC.Send(ObjC.Send(cls, ObjC.Selector("alloc")), ObjC.Selector("init"));
            }
            ObjC.SendVoid(center, ObjC.Selector("setDelegate:"), s_delegate);
        }
    }

    // ---- Callbacks from the OS. An exception escaping any of these would kill the process. ----

    [UnmanagedCallersOnly]
    private static void OnAuthorization(IntPtr block, byte granted, IntPtr error)
    {
        var completion = ObjCBlock.GetState<TaskCompletionSource<(bool, string?)>>(block);
        try
        {
            completion.TrySetResult((granted != 0, ObjC.ErrorDescription(error)));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnAdded(IntPtr block, IntPtr error)
    {
        var completion = ObjCBlock.GetState<TaskCompletionSource>(block);
        try
        {
            if (error == IntPtr.Zero)
                completion.TrySetResult();
            else
                completion.TrySetException(new InvalidOperationException($"macOS refused the notification: {ObjC.ErrorDescription(error)}"));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// Without this the OS shows nothing while the app is frontmost, which is exactly when someone
    /// clicking "Show" in the app is looking for it.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void WillPresent(IntPtr self, IntPtr cmd, IntPtr center, IntPtr notification, IntPtr completionHandler)
    {
        // Sound is in the options, but a silent notification has no sound set, so none plays.
        ((delegate* unmanaged<IntPtr, nuint, void>)ObjCBlock.GetInvoke(completionHandler))(
            completionHandler, PresentBanner | PresentList | PresentSound);
    }

    [UnmanagedCallersOnly]
    private static void DidReceiveResponse(IntPtr self, IntPtr cmd, IntPtr center, IntPtr response, IntPtr completionHandler)
    {
        try
        {
            var actionIdentifier = ObjC.ToManagedString(ObjC.Send(response, ObjC.Selector("actionIdentifier")));
            var notification = ObjC.Send(response, ObjC.Selector("notification"));
            var request = ObjC.Send(notification, ObjC.Selector("request"));
            var requestIdentifier = ObjC.ToManagedString(ObjC.Send(request, ObjC.Selector("identifier")));
            if (actionIdentifier is not null && requestIdentifier is not null)
                Volatile.Read(ref s_sink)?.OnResponse(requestIdentifier, actionIdentifier);
        }
        catch (Exception ex)
        {
            // An app's event handler threw. Letting it unwind into the OS would kill the process.
            System.Diagnostics.Trace.TraceError($"Hark: a notification response handler threw: {ex}");
        }
        finally
        {
            ((delegate* unmanaged<IntPtr, void>)ObjCBlock.GetInvoke(completionHandler))(completionHandler);
        }
    }
}
