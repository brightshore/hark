using AwesomeAssertions;
using Hark.Windows;

namespace Hark.Tests;

[TestClass]
public sealed class ToastArgumentsTests
{
    [TestMethod]
    [DataRow("t:inbox", null)]
    [DataRow("t:a&b=c?d|e%f", "id&with=odd?chars")]
    [DataRow("t:ünïcødé ✓", "réply")]
    [DataRow("g:0123456789abcdef", "reply")]
    public void Round_trips_any_tag_and_action(string tag, string? action)
    {
        ToastArguments.Parse(ToastArguments.Format(tag, action)).Should().Be((tag, action));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("hark:default")]
    [DataRow("hark?action=reply")]
    [DataRow("launch=1&tag=x")]
    public void Rejects_arguments_hark_did_not_write(string? arguments)
    {
        ToastArguments.Parse(arguments).Should().BeNull();
    }

    [TestMethod]
    public void The_activator_clsid_is_stable_per_app_and_a_version_5_guid()
    {
        var clsid = ToastArguments.ActivatorClsid("dev.brightshore.HarkSample");

        ToastArguments.ActivatorClsid("dev.brightshore.HarkSample").Should().Be(clsid);
        ToastArguments.ActivatorClsid("dev.brightshore.Other").Should().NotBe(clsid);
        var text = clsid.ToString("D");
        text[14].Should().Be('5', "the version nibble of a name-based SHA-1 GUID");
        "89ab".Should().Contain(text[19].ToString(), "the RFC 4122 variant bits");
    }
}
