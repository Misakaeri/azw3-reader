using Azw3Reader.Core.Models;

namespace Azw3Reader.Core.Parsers;

/// <summary>解析 Record 0 中的 PalmDoc 头 + MOBI 头 + EXTH 记录。</summary>
/// <remarks>
/// Record 0 结构:
///   [0x00-0x0F] PalmDoc Header (16 字节)
///   [0x10-0x13] "MOBI" 签名 (4 字节)
///   [0x14-...]  MOBI 头字段 (相对于 "MOBI" 签名的偏移来定义)
///   [EXTH 位置] EXTH 扩展头
/// </remarks>
public class MobiParser
{
    public const int PALMDOC_HEADER_SIZE = 16;

    /// <summary>从 Record 0 的数据解析出 PalmDoc 参数、MOBI header 和 EXTH。</summary>
    public (MobiHeader header, List<ExthRecord> exth) Parse(byte[] record0)
    {
        if (record0.Length < PALMDOC_HEADER_SIZE + 4)
            throw new InvalidDataException("Record 0 数据不足。");

        var header = new MobiHeader();

        // ── PalmDoc 字段 (Record 0 偏移 0x00) ──
        header.CompressionType = BigEndian.ToUInt16(record0, 0);
        header.TextLength = BigEndian.ToUInt32(record0, 4);
        header.TextRecordCount = BigEndian.ToUInt16(record0, 8);
        header.TextRecordSize = BigEndian.ToUInt16(record0, 10);

        // ── MOBI Header ──
        // 从偏移 0x10 开始: "MOBI" 签名 (4 字节), 然后是真实字段
        int mobiSig = PALMDOC_HEADER_SIZE;        // 16: "MOBI" 签名起始
        int mobiField = mobiSig + 4;               // 20: 字段起始

        // 从字段起始 (mobiField) 读取:
        header.HeaderLength = BigEndian.ToUInt32(record0, mobiField + 0);   // 0x108=264
        header.MobiType = BigEndian.ToUInt32(record0, mobiField + 4);
        header.TextEncoding = BigEndian.ToUInt32(record0, mobiField + 8);

        // 加密类型 (在 PalmDoc 头偏移 12):
        header.EncryptionType = BigEndian.ToUInt16(record0, 12);
        if (header.EncryptionType == 0)
        {
            // 有些文件加密标志在 MOBI 头偏移 0x0C
            header.EncryptionType = BigEndian.ToUInt16(record0, mobiField + 0x08);
        }

        // First Image Index: 位于 "MOBI" + 0x44 = mobiSig + 0x44
        // 部分文件此值可能无效 (如 788 > 记录总数)
        if (record0.Length > mobiSig + 0x44 + 4)
        {
            header.FirstImageIndex = BigEndian.ToUInt32(record0, mobiSig + 0x44);
        }

        // EXTH 偏移: 位于 "MOBI" + 0x50, 相对于 "MOBI" 签名起始
        uint exthRelative = 0;
        if (record0.Length > mobiSig + 0x50 + 4)
            exthRelative = BigEndian.ToUInt32(record0, mobiSig + 0x50);

        uint exthAbs = exthRelative > 0 ? (uint)(mobiSig + exthRelative) : 0;
        var exth = ParseExth(record0, exthAbs);

        // KF8 section boundary: 位于 "MOBI" + 0xE4
        if (record0.Length > mobiSig + 0xE4 + 4)
            header.Kf8SectionOffset = BigEndian.ToUInt32(record0, mobiSig + 0xE4);

        return (header, exth);
    }

    private List<ExthRecord> ParseExth(byte[] data, uint exthAbs)
    {
        var result = new List<ExthRecord>();
        if (exthAbs == 0 || exthAbs + 12 > data.Length) return result;

        string sig = System.Text.Encoding.ASCII.GetString(data, (int)exthAbs, 4);
        if (sig != "EXTH") return result;

        uint recordCount = BigEndian.ToUInt32(data, (int)exthAbs + 8);
        int pos = (int)exthAbs + 12;

        for (int i = 0; i < recordCount; i++)
        {
            if (pos + 8 > data.Length) break;
            uint tag = BigEndian.ToUInt32(data, pos);
            uint length = BigEndian.ToUInt32(data, pos + 4);

            int payloadLen = (int)length - 8;
            if (payloadLen <= 0 || pos + 8 + payloadLen > data.Length)
            {
                pos += (int)length;
                continue;
            }

            var raw = new byte[payloadLen];
            Buffer.BlockCopy(data, pos + 8, raw, 0, payloadLen);
            result.Add(new ExthRecord { Tag = (ExthTagType)tag, Length = length, RawData = raw });
            pos += (int)length;
        }
        return result;
    }
}
