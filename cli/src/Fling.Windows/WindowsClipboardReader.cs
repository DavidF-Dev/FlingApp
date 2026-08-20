using System.Text;
using System.Text.RegularExpressions;

namespace Fling.Content;

public sealed partial class WindowsClipboardReader : IClipboardReader
{
    private const string HtmlFormatName = "HTML Format";
    private const string ExcludeFromMonitoringFormatName = "ExcludeClipboardContentFromMonitorProcessing";
    private const string ClipboardHistoryFormatName = "CanIncludeInClipboardHistory";

    public ClipboardReadResult Read() =>
        Win32Clipboard.WithClipboard(() => ReadFrom(new Win32ClipboardSource()))
        ?? ClipboardReadResult.Empty;

    /// <summary>
    /// Selects the representation the receiving device can actually use: image, then
    /// plain text, then markup only as a last resort.
    /// </summary>
    /// <remarks>
    /// Markup is deliberately ranked below plain text. An application that offers both is
    /// describing the same content twice, and its CF_HTML carries whatever styling the
    /// source view happened to use — copying one word out of a syntax-highlighted diff
    /// yields hundreds of characters of span markup. The phone writes whatever arrives
    /// straight to its clipboard as plain text, so that markup would be pasted verbatim,
    /// tags and all. Preferring CF_UNICODETEXT gives the same result every other
    /// application shows.
    /// </remarks>
    internal static ClipboardReadResult ReadFrom(IClipboardSource source)
    {
        var content = ReadContent(source);
        return content is null
            ? ClipboardReadResult.Empty
            : new ClipboardReadResult(content, IsProtected(source));
    }

    /// <summary>
    /// Reports whether the clipboard's owner opted out of history and monitoring, which
    /// password managers do for the entries they copy.
    /// </summary>
    private static bool IsProtected(IClipboardSource source)
    {
        var exclude = source.RegisterFormat(ExcludeFromMonitoringFormatName);
        if (exclude != 0 && source.IsFormatAvailable(exclude))
            return true;

        var history = source.RegisterFormat(ClipboardHistoryFormatName);
        if (history == 0 || !source.IsFormatAvailable(history))
            return false;

        // The payload is a DWORD; zero is an explicit refusal, anything else consent.
        var value = source.GetBytes(history);
        return value is { Length: >= 4 } && BitConverter.ToUInt32(value, 0) == 0;
    }

    private static ClipboardContent? ReadContent(IClipboardSource source)
    {
        var htmlFormat = source.RegisterFormat(HtmlFormatName);

        var hasImage = source.IsFormatAvailable(Win32Clipboard.CF_DIB);
        var hasText = source.IsFormatAvailable(Win32Clipboard.CF_UNICODETEXT);
        var hasHtml = htmlFormat != 0 && source.IsFormatAvailable(htmlFormat);

        if (!hasImage && !hasText && !hasHtml)
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

        // Only reached when an application offered markup and no plain-text alternative,
        // which is rare. Sending it beats sending nothing.
        if (hasHtml)
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
