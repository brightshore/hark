using System.Security.Cryptography;
using System.Text;

namespace Hark.Windows;

/// <summary>
/// The activation argument string a toast carries, and the activator CLSID an app gets. Pure
/// functions with no platform restriction, so they are tested on every OS.
/// </summary>
/// <remarks>
/// The COM activator is told only the argument string of what was clicked, not which toast it
/// came from, so the toast's tag travels inside the string:
/// <c>hark?tag=&lt;escaped&gt;</c> for a body click, plus <c>&amp;action=&lt;escaped&gt;</c> for a button.
/// </remarks>
internal static class ToastArguments
{
    private const string Prefix = "hark?";

    public static string Format(string toastTag, string? actionId)
    {
        var text = Prefix + "tag=" + Uri.EscapeDataString(toastTag);
        return actionId is null ? text : text + "&action=" + Uri.EscapeDataString(actionId);
    }

    /// <summary>The tag and action of Hark's own arguments; null for anything else.</summary>
    public static (string ToastTag, string? ActionId)? Parse(string? arguments)
    {
        if (arguments is null || !arguments.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        string? tag = null, action = null;
        foreach (var pair in arguments[Prefix.Length..].Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
                continue;
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            switch (pair[..separator])
            {
                case "tag": tag = value; break;
                case "action": action = value; break;
            }
        }
        return string.IsNullOrEmpty(tag) ? null : (tag, action);
    }

    // A fixed namespace for Hark's name-based activator CLSIDs. Arbitrary, but must never change:
    // the CLSID is written to the registry and must be the same on every run.
    private static readonly Guid ActivatorNamespace = new("2f6a6c1e-3d0b-4c8e-9f4a-6b8d1e5a7c33");

    /// <summary>
    /// The app's toast activator CLSID: an RFC 4122 version-5 GUID from its AUMID. Stable across
    /// runs and machines, distinct per app, and nothing to configure.
    /// </summary>
    public static Guid ActivatorClsid(string appId)
    {
        var data = ActivatorNamespace.ToByteArray(bigEndian: true).Concat(Encoding.UTF8.GetBytes(appId)).ToArray();
        var hash = SHA1.HashData(data).AsSpan(0, 16).ToArray();
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash, bigEndian: true);
    }
}
