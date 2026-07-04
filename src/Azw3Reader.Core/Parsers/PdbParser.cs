using Azw3Reader.Core.Models;

namespace Azw3Reader.Core.Parsers;

/// <summary>解析 PDB (Palm Database) 文件头。</summary>
public class PdbParser
{
    /// <summary>PDB Header 固定长度。</summary>
    public const int PDB_HEADER_SIZE = 78;

    /// <summary>每条记录表项 8 字节。</summary>
    public const int PDB_RECORD_ENTRY_SIZE = 8;

    public PdbHeader Parse(byte[] data)
    {
        if (data.Length < PDB_HEADER_SIZE)
            throw new InvalidDataException("文件太小，不是有效的 AZW3/MOBI 文件。");

        // PDB Header 结构:
        // [0-31]  数据库名 (32 字节, 可读名称, 如书名)
        // [60-63] 数据库类型 (4 字节) — AZW3/MOBI 为 "BOOK"
        // [64-67] Creator ID  (4 字节) — AZW3/MOBI 为 "MOBI"
        // [76-77] 记录数量 (2 字节)
        string dbName = System.Text.Encoding.ASCII.GetString(data, 0, 32).TrimEnd('\0');
        string type = System.Text.Encoding.ASCII.GetString(data, 60, 4);
        string creator = System.Text.Encoding.ASCII.GetString(data, 64, 4);
        string dbType = type + creator; // 应为 "BOOKMOBI"
        int numRecords = BigEndian.ToUInt16(data, 76);

        var records = new List<PdbRecordEntry>();
        int entryStart = PDB_HEADER_SIZE;

        for (int i = 0; i < numRecords; i++)
        {
            int pos = entryStart + i * PDB_RECORD_ENTRY_SIZE;
            if (pos + 8 > data.Length) break;

            var entry = new PdbRecordEntry
            {
                Offset = BigEndian.ToUInt32(data, pos),
                Attributes = BigEndian.ToUInt32(data, pos + 4),
                Index = i
            };
            records.Add(entry);
        }

        // 计算每条记录的结束偏移
        for (int i = 0; i < records.Count; i++)
        {
            if (i + 1 < records.Count)
                records[i].EndOffset = records[i + 1].Offset;
            else
                records[i].EndOffset = (uint)data.Length;
        }

        return new PdbHeader
        {
            Name = dbName,
            DbType = dbType,
            NumRecords = (ushort)records.Count,
            Records = records
        };
    }
}
