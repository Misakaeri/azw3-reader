namespace Azw3Reader.Core.Models;

/// <summary>PDB (Palm Database) 文件头 — AZW3/MOBI 的外层容器。</summary>
public class PdbHeader
{
    /// <summary>数据库名 (PDB 偏移 0, 32 字节, 仅作显示用)。</summary>
    public string Name { get; set; } = "";
    /// <summary>类型+Creator, 期望 "BOOKMOBI" (偏移 60, 8 字节)。</summary>
    public string DbType { get; set; } = "";
    public ushort NumRecords { get; set; }
    public List<PdbRecordEntry> Records { get; set; } = [];
}

public class PdbRecordEntry
{
    /// <summary>记录在文件中的绝对偏移。</summary>
    public uint Offset { get; set; }
    public uint Attributes { get; set; }
    public int Index { get; set; }  // 记录编号

    public uint EndOffset { get; set; } // 下一条记录的偏移（用于计算长度）
}
