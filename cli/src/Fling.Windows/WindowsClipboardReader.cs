using System.Text;
using System.Text.RegularExpressions;

namespace Fling.Content;

public sealed partial class WindowsClipboardReader : IClipboardReader
{
    private const string HtmlFormatName = "HTML Format";

    public ClipboardContent? Read() =>
        Win32Clipboard.WithClipboard(() => ReadFrom(new Win32ClipboardSource()));

    /// <summary>
    /// Selects the richest available representation: image, then rich text, then plain
    /// text. Text is required for the clipboard to be considered readable at all, which
    /// matches how Windows populates formats for content Fling can send.
    /// </summary>
    internal static ClipboardContent? ReadFrom(IClipboardSource source)
    {
        var htmlFormat = source.RegisterFormat(HtmlFormatName);

        var hasImage = source.IsFormatAvailable(Win32Clipboard.CF_DIB);
        var hasText = source.IsFormatAvailable(Win32Clipboard.CF_UNICODETEXT);

        if (!hasImage && !hasText)
            return null;

        if (hasImage)
        {
            var dib = source.GetBytes(Win32Clipboard.CF_DIB);
            if (dib is not null)
            {
                return new ClipboardContent
                {
                    ContentType = "image/png",
                    Data = DibConverter.ToPng(dib),
                };
            }
        }

        if (htmlFormat != 0 && source.IsFormatAvailable(htmlFormat))
        {
            var bytes = source.GetBytes(htmlFormat);
            if (bytes is not null)
            {
                var fragment = ExtractHtmlFragment(DecodeUtf8(bytes));
                if (!string.IsNullOrWhiteSpace(fragment))
                {
                    return new ClipboardContent
                    {
                        ContentType = "text/html",
                        Data = Encoding.UTF8.GetBytes(fragment),
                    };
                }
            }
        }

        if (hasText)
        {
            var bytes = source.GetBytes(Win32Clipboard.CF_UNICODETEXT);
            if (bytes is not null)
            {
                var text = DecodeUtf16(bytes);
                if (!string.IsNullOrEmpty(text))
                {
                    return new ClipboardContent
                    {
                        ContentType = "text/plain",
                        Data = Encoding.UTF8.GetBytes(text),
                    };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// CF_HTML is defined as UTF-8 and terminated by a null byte, which is included in
    /// the reported allocation size.
    /// </summary>
    private static string DecodeUtf8(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end >= 0 ? end : bytes.Length);
    }

    private static string DecodeUtf16(byte[] bytes)
    {
        var text = Encoding.Unicode.GetString(bytes, 0, bytes.Length - (bytes.Length % 2));
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    internal static string ExtractHtmlFragment(string cfHtml)
    {
        var match = FragmentPattern().Match(cfHtml);
        if (!match.Success)
            return cfHtml;

        var startFragment = int.Parse(match.Groups[1].Value);
        var endFragment = int.Parse(match.Groups[2].Value);

        if (startFragment >= 0 && endFragment > startFragment && endFragment <= cfHtml.Length)
            return cfHtml[startFragment..endFragment];

        return cfHtml;
    }

    [GeneratedRegex(@"StartFragment:(\d+).*?EndFragment:(\d+)", RegexOptions.Singleline)]
    private static partial Regex FragmentPattern();
}
