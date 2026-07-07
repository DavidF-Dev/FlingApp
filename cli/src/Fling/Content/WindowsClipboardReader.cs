using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;

namespace Fling.Content;

public sealed partial class WindowsClipboardReader : IClipboardReader
{
    public ClipboardContent? Read()
    {
        ClipboardContent? result = null;

        var thread = new Thread(() => result = ReadOnStaThread());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static ClipboardContent? ReadOnStaThread()
    {
        if (!System.Windows.Forms.Clipboard.ContainsImage()
            && !System.Windows.Forms.Clipboard.ContainsText())
            return null;

        if (System.Windows.Forms.Clipboard.ContainsImage())
        {
            using var image = System.Windows.Forms.Clipboard.GetImage();
            if (image is not null)
            {
                using var stream = new MemoryStream();
                image.Save(stream, ImageFormat.Png);
                return new ClipboardContent
                {
                    ContentType = "image/png",
                    Data = stream.ToArray(),
                };
            }
        }

        if (System.Windows.Forms.Clipboard.ContainsText(System.Windows.Forms.TextDataFormat.Html))
        {
            var cfHtml = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.Html);
            var fragment = ExtractHtmlFragment(cfHtml);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                return new ClipboardContent
                {
                    ContentType = "text/html",
                    Data = Encoding.UTF8.GetBytes(fragment),
                };
            }
        }

        if (System.Windows.Forms.Clipboard.ContainsText(System.Windows.Forms.TextDataFormat.UnicodeText))
        {
            var text = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.UnicodeText);
            if (!string.IsNullOrEmpty(text))
            {
                return new ClipboardContent
                {
                    ContentType = "text/plain",
                    Data = Encoding.UTF8.GetBytes(text),
                };
            }
        }

        return null;
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
