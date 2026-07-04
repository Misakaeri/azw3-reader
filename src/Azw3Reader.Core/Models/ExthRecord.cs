namespace Azw3Reader.Core.Models;

/// <summary>EXTH（扩展头）中的单条记录。</summary>
public class ExthRecord
{
    public ExthTagType Tag { get; set; }
    public uint Length { get; set; }
    public byte[] RawData { get; set; } = [];

    public string GetString() => System.Text.Encoding.UTF8.GetString(RawData).TrimEnd('\0');
    public uint GetUInt32() => BigEndian.ToUInt32(RawData, 0);
}

public enum ExthTagType : uint
{
    Author = 100,
    Publisher = 101,
    Description = 103,
    Isbn = 104,
    Subject = 105,
    PublishingDate = 106,
    Title = 503,
    Language = 524,
    Asin = 113,
    CoverIndex = 201,   // 封面图片的记录号
    UpdatedAt = 551,
    CdeType = 501,
    CdeContent = 502,
    MobiVersion = 533,
}
