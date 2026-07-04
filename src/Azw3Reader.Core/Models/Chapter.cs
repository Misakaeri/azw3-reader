namespace Azw3Reader.Core.Models;

/// <summary>一个章节：标题 + 对应的 HTML 片段。</summary>
public class Chapter
{
    public string Title { get; set; } = "";
    public int Index { get; set; }
    /// <summary>该章节的 HTML 内容（不含 <html>/<body> 骨架）。</summary>
    public string HtmlContent { get; set; } = "";
    /// <summary>当前章节在完整 HTML 中的起始锚点（若有）。</summary>
    public string Anchor { get; set; } = "";
}

/// <summary>AZW3 解包的完整结果。</summary>
public class ExtractionResult
{
    public BookInfo BookInfo { get; set; } = new();
    public List<Chapter> Chapters { get; set; } = [];
    public string FullHtml { get; set; } = "";
    public Dictionary<int, ImageRecord> Images { get; set; } = [];
}

public class ImageRecord
{
    public byte[] Data { get; set; } = [];
    public string MimeType { get; set; } = "image/jpeg";
}
