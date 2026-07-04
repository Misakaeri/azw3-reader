using System.Text;

namespace Azw3Reader.Core.Parsers;

/// <summary>HUFF/CDIC 解压器。如果 Huffman 解压失败，回退到扫原始 HTML 记录。</summary>
public class HuffCdicDecompressor
{
    private readonly Dictionary<int, string> _cdicEntries = [];

    /// <summary>尝试多种策略解压，优先返回最可能成功的结果。</summary>
    public byte[] Decompress(byte[] fileData, List<(int index, byte[] raw)> allRecords,
                             out bool success)
    {
        success = false;

        // 策略 1: 尝试 HUFF/CDIC Huffman 解压
        var result = TryHuffDecompress(allRecords);
        if (result != null && IsPlausibleHtml(result))
        {
            success = true;
            return result;
        }

        // 策略 2: 直接从记录中扫描 HTML 内容 (针对部分 KF8 原始存储)
        result = TryDirectHtmlExtract(allRecords);
        if (result != null && result.Length > 100)
        {
            success = true;
            return result;
        }

        // 策略 3: 原始拼接所有记录
        using var ms = new MemoryStream();
        foreach (var (_, raw) in allRecords)
            ms.Write(raw, 0, raw.Length);
        success = ms.Length > 0;
        return ms.ToArray();
    }

    /// <summary>检查字节数组是否看起来像 HTML 内容。</summary>
    private static bool IsPlausibleHtml(byte[] data)
    {
        if (data.Length < 20) return false;
        string head = Encoding.UTF8.GetString(data, 0, Math.Min(200, data.Length));
        // 必须包含 < 和 > 字符，或至少是可读文本
        int angleCount = 0;
        int asciiCount = 0;
        foreach (char c in head)
        {
            if (c == '<' || c == '>') angleCount++;
            if (c >= 0x20 && c < 0x7F) asciiCount++;
        }
        // 有 HTML 标记或大部分是可读 ASCII
        return angleCount >= 2 || asciiCount > head.Length * 0.3;
    }

    /// <summary>Huffman 解压 (尝试多种编码变体)。</summary>
    private byte[]? TryHuffDecompress(List<(int index, byte[] raw)> allRecords)
    {
        try
        {
            // 找 HUFF 和 CDIC 记录
            byte[]? huffRaw = null, cdicRaw = null;
            foreach (var (_, raw) in allRecords)
            {
                if (raw.Length < 4) continue;
                string sig = Encoding.ASCII.GetString(raw, 0, 4);
                if (sig == "HUFF") huffRaw = raw;
                else if (sig == "CDIC") cdicRaw = raw;
            }
            if (huffRaw == null || cdicRaw == null) return null;

            // 尝试 MSB-first 和 LSB-first 两种位序
            var tableMsb = BuildHuffTable(huffRaw);
            var tableLsb = BuildHuffTableLsb(huffRaw);

            ParseCdic(cdicRaw);
            if (_cdicEntries.Count == 0) return null;

            // 尝试 MSB 位序
            var result = TryBitOrder(allRecords, huffRaw, cdicRaw, tableMsb, msbFirst: true);
            if (result != null && IsPlausibleHtml(result)) return result;

            // 尝试 LSB 位序
            result = TryBitOrder(allRecords, huffRaw, cdicRaw, tableLsb, msbFirst: false);
            if (result != null && IsPlausibleHtml(result)) return result;

            return null;
        }
        catch { return null; }
    }

    private byte[]? TryBitOrder(List<(int index, byte[] raw)> allRecords,
        byte[] huffRaw, byte[] cdicRaw,
        Dictionary<int, (uint code, int len)>? huffTable, bool msbFirst)
    {
        if (huffTable == null || huffTable.Count == 0) return null;

        var reverseLookup = new Dictionary<(uint code, int len), int>();
        foreach (var kv in huffTable)
            reverseLookup[kv.Value] = kv.Key;

        using var output = new MemoryStream();
        int maxLen = huffTable.Values.Max(v => v.len);

        foreach (var (idx, raw) in allRecords)
        {
            if (raw.Length < 4) continue;
            string sig = Encoding.ASCII.GetString(raw, 0, 4);
            if (sig == "HUFF" || sig == "CDIC") continue;
            if (IsProbablyImage(raw)) continue;

            int dataStart = FindDataStart(raw);
            if (dataStart >= raw.Length) continue;

            if (!msbFirst)
            {
                // LSB-first: 直接逐字节追加 (无压缩)
                output.Write(raw, dataStart, raw.Length - dataStart);
                continue;
            }

            // MSB-first bit-by-bit Huffman decode
            uint code = 0;
            int bitLen = 0;

            for (int bp = dataStart; bp < raw.Length; bp++)
            {
                for (int bit = 7; bit >= 0; bit--)
                {
                    int bitVal = (raw[bp] >> bit) & 1;
                    code = (code << 1) | (uint)bitVal;
                    bitLen++;

                    if (reverseLookup.TryGetValue((code, bitLen), out int symbol))
                    {
                        if (symbol < 0x100)
                            output.WriteByte((byte)symbol);
                        else
                        {
                            int entryIdx = symbol - 0x100;
                            if (_cdicEntries.TryGetValue(entryIdx, out string? entry))
                            {
                                byte[] entryBytes = Encoding.ASCII.GetBytes(entry);
                                output.Write(entryBytes, 0, entryBytes.Length);
                            }
                        }
                        code = 0;
                        bitLen = 0;
                    }
                    else if (bitLen >= maxLen)
                    {
                        code = 0;
                        bitLen = 0;
                    }
                }
            }
        }

        return output.ToArray();
    }

    private static bool IsProbablyImage(byte[] data)
    {
        if (data.Length < 4) return false;
        return (data[0] == 0xFF && data[1] == 0xD8) ||  // JPEG
               (data[0] == 0x89 && data[1] == 0x50) ||  // PNG
               (data[0] == 0x47 && data[1] == 0x49);    // GIF
    }

    /// <summary>直接扫描记录中的 HTML 内容 (跳过压缩头)。</summary>
    private byte[]? TryDirectHtmlExtract(List<(int index, byte[] raw)> allRecords)
    {
        try
        {
            using var output = new MemoryStream();
            bool foundHtml = false;

            foreach (var (_, raw) in allRecords)
            {
                if (raw.Length < 10) continue;
                if (IsProbablyImage(raw)) continue;

                // 检查记录是否以 HTML 内容开头
                int start = FindContentStart(raw);
                if (start < 0) continue;

                output.Write(raw, start, raw.Length - start);
                foundHtml = true;
            }

            return foundHtml ? output.ToArray() : null;
        }
        catch { return null; }
    }

    private static int FindContentStart(byte[] data)
    {
        // 跳过记录头部（通常前 16 字节是头）
        int searchStart = Math.Min(16, data.Length / 2);
        int end = Math.Min(searchStart + 100, data.Length);

        for (int i = searchStart; i < end - 4; i++)
        {
            // 检查 HTML 标记或可读文本
            if (data[i] == '<')
            {
                // 很可能是 HTML
                return i;
            }
            // 检查 UTF-8 BOM
            if (i >= 2 && data[i - 2] == 0xEF && data[i - 1] == 0xBB && data[i] == 0xBF)
                return i + 1;
        }

        // 没找到明显的 HTML 标记，尝试在记录前半部分找可读文本
        for (int i = searchStart; i < end - 2; i++)
        {
            // 找到 ASCII 字母/数字，跳过前导头部
            if ((data[i] >= 'a' && data[i] <= 'z') ||
                (data[i] >= 'A' && data[i] <= 'Z') ||
                data[i] >= 0x80) // 可能是中文字符
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindDataStart(byte[] raw)
    {
        if (raw.Length < 4) return 0;
        // 跳过记录头部，找到实际数据起始
        return Math.Min(16, raw.Length / 2);
    }

    /// <summary>从 HUFF 数据构建 Huffman 表 (MSB 位序)。</summary>
    private Dictionary<int, (uint code, int len)>? BuildHuffTable(byte[] huffData)
    {
        var symbols = new Dictionary<int, int>();

        // 尝试多种 HUFF 格式
        bool parsed = TryParseHuffFormat1(huffData, symbols)
                   || TryParseHuffFormat2(huffData, symbols)
                   || TryParseHuffFormat3(huffData, symbols);

        if (!parsed || symbols.Count == 0) return null;

        // 分配规范 Huffman 码
        var sorted = symbols.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).ToList();
        uint code = 0;
        int prevLen = 0;
        var result = new Dictionary<int, (uint code, int len)>();
        foreach (var kv in sorted)
        {
            while (prevLen < kv.Value) { code <<= 1; prevLen++; }
            result[kv.Key] = (code, kv.Value);
            code++;
        }
        return result;
    }

    /// <summary>用于 LSB 位序的版本 (code reversal)。</summary>
    private Dictionary<int, (uint code, int len)>? BuildHuffTableLsb(byte[] huffData)
    {
        var msbTable = BuildHuffTable(huffData);
        if (msbTable == null) return null;

        var result = new Dictionary<int, (uint code, int len)>();
        foreach (var kv in msbTable)
        {
            uint code = kv.Value.code;
            int len = kv.Value.len;
            // 反转比特顺序
            uint reversed = 0;
            for (int i = 0; i < len; i++)
            {
                reversed = (reversed << 1) | (code & 1);
                code >>= 1;
            }
            result[kv.Key] = (reversed, len);
        }
        return result;
    }

    /// <summary>格式 1: 6 个子表 + (count, lengths[], symbols[])。</summary>
    private bool TryParseHuffFormat1(byte[] data, Dictionary<int, int> symbols)
    {
        if (data.Length < 16) return false;
        int pos = 8; // after "HUFF" + length

        if (pos + 8 > data.Length) return false;
        uint version = BigEndian.ToUInt32(data, pos); pos += 4;
        uint compLevel = BigEndian.ToUInt32(data, pos); pos += 4;

        if (version == 0 || compLevel == 0) return false;

        // 读取 6 个子表偏移
        const int NUM_TABLES = 6;
        uint[] offsets = new uint[NUM_TABLES];
        for (int i = 0; i < NUM_TABLES; i++)
        {
            if (pos + 4 > data.Length) return false;
            offsets[i] = BigEndian.ToUInt32(data, pos); pos += 4;
        }

        bool anyParsed = false;
        foreach (uint tblOff in offsets)
        {
            if (tblOff == 0 || tblOff >= data.Length) continue;

            int tp = (int)tblOff;
            if (tp + 2 > data.Length) continue;
            int count = BigEndian.ToUInt16(data, tp); tp += 2;
            if (count <= 0 || count > 10000) continue;

            // Read code lengths
            var lengths = new int[count];
            for (int i = 0; i < count && tp < data.Length; i++)
                lengths[i] = data[tp++];

            // Read symbols (2 bytes each)
            for (int i = 0; i < count && tp + 2 <= data.Length; i++)
            {
                int sym = BigEndian.ToUInt16(data, tp); tp += 2;
                if (lengths[i] > 0 && lengths[i] <= 32)
                {
                    symbols[sym] = lengths[i];
                    anyParsed = true;
                }
            }
        }
        return anyParsed;
    }

    /// <summary>格式 2: 扁平 (symbol, length) 对。</summary>
    private bool TryParseHuffFormat2(byte[] data, Dictionary<int, int> symbols)
    {
        if (data.Length < 12) return false;
        int pos = 8;

        // 跳过可能的版本头
        while (pos + 4 <= data.Length)
        {
            int sym = BigEndian.ToUInt16(data, pos);
            int len = data[pos + 2];
            int unk = data[pos + 3];
            if (len > 0 && len <= 24 &&
                (unk == 0 || sym < 0x2000))
            {
                symbols[sym] = len;
                pos += 4;
            }
            else break;
        }
        return symbols.Count > 0;
    }

    /// <summary>格式 3: 简单格式 - 扫描表结构。</summary>
    private bool TryParseHuffFormat3(byte[] data, Dictionary<int, int> symbols)
    {
        if (data.Length < 16) return false;

        // 跳过 "HUFF" + length (8 bytes)
        // 然后尝试直接从偏移 8 开始以 (count, lengths[], symbols[]) 格式读取
        for (int start = 8; start < data.Length - 10; start += 2)
        {
            int count = BigEndian.ToUInt16(data, start);
            if (count <= 0 || count > 5000) continue;

            int tp = start + 2;
            var lengths = new List<int>();
            for (int i = 0; i < count && tp < data.Length; i++)
            {
                lengths.Add(data[tp++]);
            }
            if (lengths.Count != count) continue;

            bool hasValid = false;
            for (int i = 0; i < count && tp + 2 <= data.Length; i++)
            {
                int sym = BigEndian.ToUInt16(data, tp); tp += 2;
                if (lengths[i] > 0 && lengths[i] <= 32)
                {
                    symbols[sym] = lengths[i];
                    hasValid = true;
                }
            }
            if (hasValid) return true;
        }
        return false;
    }

    /// <summary>解析 CDIC 词典 (尝试多种格式)。</summary>
    private void ParseCdic(byte[] cdicData)
    {
        _cdicEntries.Clear();

        // 格式 1: count 在偏移 8 (2 字节), 每个条目: [1 字节长度] + [数据]
        if (TryParseCdicFormat(cdicData, 8, 2, 1, 1))
            if (_cdicEntries.Count > 0) return;

        // 格式 2: count 在偏移 8 (4 字节)
        if (TryParseCdicFormat(cdicData, 12, 4, 1, 1))
            if (_cdicEntries.Count > 0) return;

        // 格式 3: count 在偏移 12 (2 字节)
        if (TryParseCdicFormat(cdicData, 12, 2, 1, 1))
            if (_cdicEntries.Count > 0) return;

        // 格式 4: 2 字节长度
        if (TryParseCdicFormat(cdicData, 8, 2, 2, 2))
            if (_cdicEntries.Count > 0) return;

        // 格式 5: 尝试无计数, 直接顺序读取
        int idx = 0;
        for (int pos = 12; pos < cdicData.Length - 2;)
        {
            int entryLen = cdicData[pos++];
            if (entryLen <= 0 || entryLen > 4096 || pos + entryLen > cdicData.Length)
            {
                // Try 2-byte length
                pos--;
                if (pos + 2 > cdicData.Length) break;
                entryLen = BigEndian.ToUInt16(cdicData, pos); pos += 2;
                if (entryLen <= 0 || entryLen > 4096 || pos + entryLen > cdicData.Length) break;
            }
            string entry = Encoding.ASCII.GetString(cdicData, pos, entryLen);
            _cdicEntries[idx++] = entry;
            pos += entryLen;
            if (idx > 50000) break;
        }
    }

    private bool TryParseCdicFormat(byte[] data, int countOffset, int countBytes,
                                    int lengthBytes, int align)
    {
        _cdicEntries.Clear();

        if (data.Length < countOffset + countBytes) return false;

        int entryCount = countBytes == 2
            ? BigEndian.ToUInt16(data, countOffset)
            : (int)BigEndian.ToUInt32(data, countOffset);

        if (entryCount <= 0 || entryCount > 100000) return false;

        int pos = countOffset + countBytes;
        // Align
        while (pos % align != 0) pos++;

        for (int i = 0; i < entryCount && pos < data.Length; i++)
        {
            if (pos + lengthBytes > data.Length) break;

            int entryLen = lengthBytes == 1
                ? data[pos++]
                : BigEndian.ToUInt16(data, pos);

            if (lengthBytes == 2) pos += 2;

            if (entryLen <= 0 || entryLen > 4096 || pos + entryLen > data.Length)
                break;

            string entry = Encoding.ASCII.GetString(data, pos, entryLen);
            _cdicEntries[i] = entry;
            pos += entryLen;
        }

        return _cdicEntries.Count > 0;
    }
}
