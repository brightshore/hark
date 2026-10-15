using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Hark.Windows;

namespace Hark.Tests;

/// <summary>
/// The event-handler IIDs in <see cref="ToastEventIids"/> are WinRT parameterized-interface IIDs,
/// which are derived, not assigned. These recompute them with the documented algorithm (a SHA-1
/// name-based GUID over the type signature) from the interface IIDs, so a wrong constant cannot
/// survive: Windows would answer E_NOINTERFACE and no toast event would ever arrive.
/// </summary>
[TestClass]
public sealed class WinRTIidTests
{
    private static readonly Guid WinRTNamespace = new("11f47ad5-7b73-42c0-abae-878b1e16adee");
    private const string TypedEventHandler = "{9de1c534-6ae1-11e0-84e1-18a905bcc53f}";
    private const string ToastNotification = "rc(Windows.UI.Notifications.ToastNotification;{997e2675-059e-4e60-8b06-1760917c8b80})";

    private static Guid ParameterizedIid(string signature)
    {
        var data = WinRTNamespace.ToByteArray(bigEndian: true).Concat(Encoding.UTF8.GetBytes(signature)).ToArray();
        var hash = SHA1.HashData(data).AsSpan(0, 16).ToArray();
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash, bigEndian: true);
    }

    [TestMethod]
    public void The_algorithm_reproduces_a_published_iid()
    {
        // IReference<Int32>, as published in the Windows SDK headers.
        ParameterizedIid("pinterface({61c17706-2d65-11e0-9ae8-d48564015472};i4)")
            .Should().Be(new Guid("548cefbd-bc8a-5fa0-8df2-957440fc8bf4"));
    }

    [TestMethod]
    public void Toast_event_handler_iids_match_their_signatures()
    {
        ParameterizedIid($"pinterface({TypedEventHandler};{ToastNotification};cinterface(IInspectable))")
            .Should().Be(ToastEventIids.Activated);
        ParameterizedIid($"pinterface({TypedEventHandler};{ToastNotification};rc(Windows.UI.Notifications.ToastDismissedEventArgs;{{3f89d935-d9cb-4538-a0f0-ffe7659938f8}}))")
            .Should().Be(ToastEventIids.Dismissed);
        ParameterizedIid($"pinterface({TypedEventHandler};{ToastNotification};rc(Windows.UI.Notifications.ToastFailedEventArgs;{{35176862-cfd4-44f8-ad64-f500fd896c3b}}))")
            .Should().Be(ToastEventIids.Failed);
    }
}
