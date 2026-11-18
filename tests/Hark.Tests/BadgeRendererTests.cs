using AwesomeAssertions;
using Hark.Windows;

namespace Hark.Tests;

/// <summary>The count Windows shows on the taskbar button, checked pixel by pixel.</summary>
[TestClass]
public sealed class BadgeRendererTests
{
    private static byte Alpha(uint pixel) => (byte)(pixel >> 24);

    private static bool IsInk(uint pixel) => (pixel & 0xFFFFFF) == BadgeRenderer.InkRgb;

    [TestMethod]
    [DataRow(1, "1")]
    [DataRow(99, "99")]
    [DataRow(100, "99+")]
    [DataRow(5000, "99+")]
    public void Counts_above_99_read_99_plus(int count, string text)
        => BadgeRenderer.CountText(count).Should().Be(text);

    [TestMethod]
    public void The_disc_fills_the_square_with_clear_corners()
    {
        var icon = BadgeRenderer.RenderCount(3, 16);

        icon.Pixels.Should().HaveCount(16 * 16);
        Alpha(icon.Pixels[0]).Should().Be(0);
        Alpha(icon.Pixels[15]).Should().Be(0);
        Alpha(icon.Pixels[15 * 16]).Should().Be(0);
        Alpha(icon.Pixels[16 * 16 - 1]).Should().Be(0);
        Alpha(icon.Pixels[8 * 16 + 1]).Should().Be(255, "the disc reaches the left edge at mid-height");
        icon.Pixels.Where(p => Alpha(p) is > 0 and < 255).Should().NotBeEmpty("the edge is anti-aliased");
        icon.Pixels.Where(p => Alpha(p) > 0 && !IsInk(p)).Should().OnlyContain(p => (p & 0xFFFFFF) == BadgeRenderer.FillRgb);
    }

    [TestMethod]
    public void A_single_digit_is_the_large_glyph_centred()
    {
        var icon = BadgeRenderer.RenderCount(1, 16);

        // The 5x7 '1', centred: (16 - 5) / 2 = 5 across, (16 - 7) / 2 = 4 down.
        string[] glyph = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"];
        var expected = new HashSet<(int X, int Y)>();
        for (var row = 0; row < glyph.Length; row++)
            for (var column = 0; column < glyph[row].Length; column++)
                if (glyph[row][column] == '1')
                    expected.Add((5 + column, 4 + row));

        var ink = new HashSet<(int X, int Y)>();
        for (var i = 0; i < icon.Pixels.Length; i++)
            if (IsInk(icon.Pixels[i]))
                ink.Add((i % 16, i / 16));

        ink.Should().BeEquivalentTo(expected);
    }

    [TestMethod]
    [DataRow("7", 16, 7, 1)]
    [DataRow("99", 16, 7, 1)]
    [DataRow("99+", 16, 5, 1)]
    [DataRow("7", 24, 7, 2)]
    [DataRow("12", 20, 5, 2)]
    [DataRow("99+", 32, 5, 2)]
    public void The_tallest_text_that_fits_the_disc_is_chosen(string text, int size, int fontHeight, int scale)
    {
        var (font, chosenScale) = BadgeRenderer.ChooseFont(text, size);

        (font.Height, chosenScale).Should().Be((fontHeight, scale));
    }

    /// <summary>
    /// Every digit lands where the disc is fully opaque, at every size the taskbar asks for and every
    /// text a count can become: a glyph clipped by the edge would read as a different number.
    /// </summary>
    [TestMethod]
    public void Every_count_stays_inside_the_disc_at_every_scaling()
    {
        foreach (var size in new[] { 16, 20, 24, 28, 32, 40, 48 })
        {
            foreach (var count in new[] { 1, 8, 10, 88, 99, 100 })
            {
                var icon = BadgeRenderer.RenderCount(count, size);
                var ink = icon.Pixels.Where(IsInk).ToArray();
                ink.Should().NotBeEmpty($"count {count} at {size}px must draw something");
                ink.Should().OnlyContain(p => Alpha(p) == 255, $"count {count} at {size}px must not touch the disc's edge");
            }
        }
    }
}
