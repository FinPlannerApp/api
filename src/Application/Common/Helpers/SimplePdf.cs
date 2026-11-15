using System.Globalization;
using System.Text;

namespace Application.Common.Helpers;

/// <summary>
/// A tiny dependency-free PDF writer (A4, built-in Helvetica fonts). Enough for tabular reports:
/// text, lines and filled bars. Characters outside Latin-1 are written as '?'.
/// </summary>
public sealed class SimplePdf
{
    public const double PageWidth = 595;
    public const double PageHeight = 842;

    private readonly List<StringBuilder> _pages = new();
    private StringBuilder _current = null!;

    public SimplePdf() => NewPage();

    public void NewPage()
    {
        _current = new StringBuilder();
        _pages.Add(_current);
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            var c = ch > 255 ? '?' : ch;
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Draws text with its baseline at y (points from the BOTTOM of the page).</summary>
    public void Text(double x, double y, string text, double size = 10, bool bold = false, bool alignRight = false, double grey = 0)
    {
        if (alignRight) x -= EstimateWidth(text, size);
        _current.Append($"BT {F(grey)} g /{(bold ? "F2" : "F1")} {F(size)} Tf {F(x)} {F(y)} Td ({Escape(text)}) Tj ET\n");
    }

    public void Line(double x1, double y1, double x2, double y2, double grey = 0.75)
        => _current.Append($"{F(grey)} G 0.5 w {F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S\n");

    public void Bar(double x, double y, double width, double height, double grey = 0.35)
        => _current.Append($"{F(grey)} g {F(x)} {F(y)} {F(Math.Max(width, 0))} {F(height)} re f\n");

    /// <summary>Rough Helvetica width, good enough to right-align numbers.</summary>
    public static double EstimateWidth(string text, double size) => text.Length * size * 0.52;

    public byte[] ToBytes()
    {
        var latin1 = Encoding.Latin1;
        var objects = new List<string>();
        var pageCount = _pages.Count;

        // 1 catalog, 2 pages, 3 Helvetica, 4 Helvetica-Bold, then (page, content) pairs.
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{5 + i * 2} 0 R"));
        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pageCount; i++)
        {
            var contentId = 6 + i * 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            var content = _pages[i].ToString();
            objects.Add($"<< /Length {latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        using var ms = new MemoryStream();
        void Write(string s) { var b = latin1.GetBytes(s); ms.Write(b, 0, b.Length); }

        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
