namespace Azw3Reader.Core.Models;

public class BookInfo
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Isbn { get; set; } = "";
    public string Asin { get; set; } = "";
    public string Language { get; set; } = "";
    public string CoverImageUrl { get; set; } = "";     // base64 data URI
    public int CoverImageIndex { get; set; } = -1;      // 封面在 Records 中的索引
    public DateTime? UpdatedAt { get; set; }
    public string Description { get; set; } = "";
    public string FileFormat { get; set; } = "";        // "AZW3" / "MOBI" / "KF8"
}
