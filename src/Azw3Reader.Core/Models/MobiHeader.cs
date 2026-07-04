namespace Azw3Reader.Core.Models;

/// <summary>MOBI/KF8 Header 中的关键字段。</summary>
public class MobiHeader
{
    /// <summary>压缩类型: 1=PalmDoc, 2=Huffman(CDIC), 17480=None</summary>
    public ushort CompressionType { get; set; }

    /// <summary>未压缩的文本总长度。</summary>
    public uint TextLength { get; set; }

    /// <summary>文本记录数量（图片之前的记录数）。</summary>
    public ushort TextRecordCount { get; set; }

    /// <summary>每条文本记录大小（通常为 4096）。</summary>
    public ushort TextRecordSize { get; set; }

    /// <summary>加密类型: 0=无, 1=旧式, 2=KF8加密</summary>
    public ushort EncryptionType { get; set; }

    /// <summary>第一条图片记录的索引。</summary>
    public uint FirstImageIndex { get; set; }

    /// <summary>MOBI Header 总长度（含自身长度字段之后）。</summary>
    public uint HeaderLength { get; set; }

    /// <summary>MOBI 类型: 2=未分类, 3=小说, 4=报刊等。</summary>
    public uint MobiType { get; set; }

    /// <summary>KF8 节区的偏移（=0 表示无 KF8）。</summary>
    public uint Kf8SectionOffset { get; set; }

    /// <summary>文本编码: 1252=Latin1, 65001=UTF-8</summary>
    public uint TextEncoding { get; set; }
}
