using System.Globalization;

namespace Hark.Windows;

/// <summary>A square icon image: <see cref="Size"/>² pixels, rows top-down, each <c>0xAARRGGBB</c> with straight alpha.</summary>
internal sealed record BadgeIcon(int Size, uint[] Pixels);

/// <summary>
/// Draws the count Windows shows on the taskbar button: white digits on a red disc. The overlay is
/// only 16 pixels at 100% scaling, so the digits are a pixel font drawn here in managed code rather
/// than text through GDI, which leaves no alpha behind on a 32-bit bitmap. That also makes every
/// pixel testable on any OS.
/// </summary>
internal static class BadgeRenderer
{
    /// <summary>Windows 11's critical red.</summary>
    internal const uint FillRgb = 0xC42B1C;
    internal const uint InkRgb = 0xFFFFFF;

    /// <summary>The largest count shown as itself; anything above reads "99+".</summary>
    internal const int MaxShownCount = 99;

    // Rows top-down, '1' is a lit pixel. 5×7 reads best; 3×5 fits "99+" into a 16-pixel disc.
    private static readonly PixelFont Large = new(5, 7, new Dictionary<char, string[]>
    {
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        ['+'] = ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
    });

    private static readonly PixelFont Small = new(3, 5, new Dictionary<char, string[]>
    {
        ['0'] = ["111", "101", "101", "101", "111"],
        ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["111", "001", "111", "100", "111"],
        ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"],
        ['7'] = ["111", "001", "001", "001", "001"],
        ['8'] = ["111", "101", "111", "101", "111"],
        ['9'] = ["111", "101", "111", "001", "111"],
        ['+'] = ["000", "010", "111", "010", "000"],
    });

    /// <summary>What the badge says for <paramref name="count"/>.</summary>
    internal static string CountText(int count)
        => count > MaxShownCount ? $"{MaxShownCount}+" : count.ToString(CultureInfo.InvariantCulture);

    public static BadgeIcon RenderCount(int count, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var pixels = new uint[size * size];
        DrawDisc(pixels, size);
        DrawText(pixels, size, CountText(count));
        return new BadgeIcon(size, pixels);
    }

    /// <summary>A disc filling the square, its edge anti-aliased by 4×4 supersampling.</summary>
    private static void DrawDisc(uint[] pixels, int size)
    {
        const int Samples = 4;
        var radius = size / 2.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inside = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var dx = x + (sx + 0.5) / Samples - radius;
                        var dy = y + (sy + 0.5) / Samples - radius;
                        if (dx * dx + dy * dy <= radius * radius)
                            inside++;
                    }
                }
                var alpha = (uint)Math.Round(inside * 255.0 / (Samples * Samples));
                pixels[y * size + x] = alpha << 24 | FillRgb;
            }
        }
    }

    private static void DrawText(uint[] pixels, int size, string text)
    {
        var (font, scale) = ChooseFont(text, size);
        var width = font.TextWidth(text) * scale;
        var height = font.Height * scale;
        var left = (size - width) / 2;
        var top = (size - height) / 2;

        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.Glyphs[text[i]];
            var glyphLeft = left + i * (font.Width + 1) * scale;
            for (var row = 0; row < font.Height; row++)
            {
                for (var column = 0; column < font.Width; column++)
                {
                    if (glyph[row][column] == '1')
                        Ink(pixels, size, glyphLeft + column * scale, top + row * scale, scale);
                }
            }
        }
    }

    /// <summary>Whitens a <paramref name="scale"/>-square block, keeping the disc's alpha so the edge stays smooth.</summary>
    private static void Ink(uint[] pixels, int size, int left, int top, int scale)
    {
        for (var y = Math.Max(0, top); y < Math.Min(size, top + scale); y++)
        {
            for (var x = Math.Max(0, left); x < Math.Min(size, left + scale); x++)
            {
                ref var pixel = ref pixels[y * size + x];
                pixel = (pixel & 0xFF000000) | InkRgb;
            }
        }
    }

    /// <summary>
    /// The font and whole-pixel scale that draw <paramref name="text"/> tallest while its box stays
    /// inside the disc and under 60% of its height. The large font wins a tie.
    /// </summary>
    internal static (PixelFont Font, int Scale) ChooseFont(string text, int size)
    {
        (PixelFont Font, int Scale) best = (Small, 1);
        var bestHeight = 0;
        foreach (var font in (ReadOnlySpan<PixelFont>)[Large, Small])
        {
            for (var scale = 1; Fits(font, text, size, scale); scale++)
            {
                if (font.Height * scale > bestHeight)
                {
                    best = (font, scale);
                    bestHeight = font.Height * scale;
                }
            }
        }
        return best;
    }

    private static bool Fits(PixelFont font, string text, int size, int scale)
    {
        var halfWidth = font.TextWidth(text) * scale / 2.0;
        var halfHeight = font.Height * scale / 2.0;
        var radius = size / 2.0;
        return font.Height * scale <= size * 0.6 && halfWidth * halfWidth + halfHeight * halfHeight <= radius * radius;
    }

    internal sealed record PixelFont(int Width, int Height, IReadOnlyDictionary<char, string[]> Glyphs)
    {
        /// <summary>Glyphs with one blank column between them, at scale 1.</summary>
        public int TextWidth(string text) => text.Length * (Width + 1) - 1;
    }
}
