using Fling.Content;

namespace Fling.Tests;

public sealed class ClipboardReaderTests
{
    [Fact]
    public void ExtractHtmlFragment_ValidCfHtml_ExtractsFragment()
    {
        // Build a realistic CF_HTML string with correct byte offsets
        var header = "Version:0.9\r\nStartHTML:<<SH>>\r\nEndHTML:<<EH>>\r\nStartFragment:<<SF>>\r\nEndFragment:<<EF>>\r\n";
        var htmlStart = "<html><body>\r\n<!--StartFragment-->";
        var fragment = "<b>hello</b>";
        var htmlEnd = "<!--EndFragment-->\r\n</body></html>";

        // Calculate offsets (header length is fixed once we fill in 8-digit numbers)
        var headerTemplate = header
            .Replace("<<SH>>", "00000000")
            .Replace("<<EH>>", "00000000")
            .Replace("<<SF>>", "00000000")
            .Replace("<<EF>>", "00000000");

        var startHtml = headerTemplate.Length;
        var startFragment = startHtml + htmlStart.Length;
        var endFragment = startFragment + fragment.Length;
        var endHtml = endFragment + htmlEnd.Length;

        var cfHtml = header
            .Replace("<<SH>>", startHtml.ToString("D8"))
            .Replace("<<EH>>", endHtml.ToString("D8"))
            .Replace("<<SF>>", startFragment.ToString("D8"))
            .Replace("<<EF>>", endFragment.ToString("D8"))
            + htmlStart + fragment + htmlEnd;

        var result = WindowsClipboardReader.ExtractHtmlFragment(cfHtml);

        Assert.Equal("<b>hello</b>", result);
    }

    [Fact]
    public void ExtractHtmlFragment_NoHeaders_ReturnsOriginal()
    {
        var html = "<b>hello</b>";

        var result = WindowsClipboardReader.ExtractHtmlFragment(html);

        Assert.Equal(html, result);
    }
}
