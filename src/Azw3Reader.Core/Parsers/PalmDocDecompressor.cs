namespace Azw3Reader.Core.Parsers;

/// <summary>PalmDoc LZ77 解压算法 (MOBI 标准实现)。</summary>
/// <remarks>
/// 算法参考: kindleunpack/mobi_uncompress.py
/// 编码规则:
///   0x01-0x08: 直接复制接下来 N 个字节作为字面值
///   0x09-0x7F: 单字面字节 (值就是自身)
///   0x80-0xBF: 2 字节回溯: 读下一字节组成编码, 距离 11 位, 长度 3-10 字节
///   0xC0-0xFF: 空格 + 字面字节 (字节值 = c ^ 0x80)
///   0x00: 已结束或特殊处理 (实际极少出现)
/// </remarks>
public class PalmDocDecompressor
{
    public byte[] Decompress(byte[] compressed, int uncompressedSize = 0)
    {
        if (compressed.Length == 0) return [];

        using var output = new MemoryStream(uncompressedSize > 0 ? uncompressedSize : compressed.Length * 3);
        DecompressCore(compressed, output);
        return output.ToArray();
    }

    /// <summary>
    /// 将压缩数据解压到已有输出流中（共享缓冲区）。
    /// PalmDoc LZ77 回溯引用可跨 record 工作，因为 output stream 包含了前面所有 record 的解压数据。
    /// </summary>
    public void DecompressContinue(byte[] compressed, MemoryStream output)
    {
        if (compressed.Length == 0) return;
        DecompressCore(compressed, output);
    }

    private static void DecompressCore(byte[] compressed, MemoryStream output)
    {
        int p = 0;

        try
        {
            while (p < compressed.Length)
            {
                int c = compressed[p++];

                if (c >= 1 && c <= 8)
                {
                    // 复制接下来的 c 个字面字节
                    for (int i = 0; i < c && p < compressed.Length; i++)
                        output.WriteByte(compressed[p++]);
                }
                else if (c < 128)
                {
                    // 0x09-0x7F: 单字面字节 (包括 0x00)
                    output.WriteByte((byte)c);
                }
                else if (c >= 192)
                {
                    // 0xC0-0xFF: 空格 + 字面字节 (异或 0x80)
                    output.WriteByte(0x20); // 空格
                    output.WriteByte((byte)(c ^ 0x80));
                }
                else
                {
                    // 0x80-0xBF: 2 字节回溯
                    if (p >= compressed.Length) break;
                    int next = compressed[p++];
                    int code = (c << 8) | next;
                    int dist = (code >> 3) & 0x7FF;  // 11 位距离
                    int len = (code & 7) + 3;         // 3-10 字节长度

                    if (dist <= 0) break;
                    long pos = output.Position;
                    int copyStart = (int)pos - dist;

                    if (copyStart >= 0)
                    {
                        for (int i = 0; i < len; i++)
                            output.WriteByte(output.GetBuffer()[copyStart + i]);
                    }
                    else
                    {
                        // 距离超出已有数据时用空格填充
                        for (int i = 0; i < len; i++)
                            output.WriteByte(0x20);
                    }
                }
            }
        }
        catch
        {
            // 部分损坏的数据截断时返回已有结果
        }
    }
}
