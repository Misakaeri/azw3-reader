namespace Azw3Reader.Core;

/// <summary>
/// 大端序辅助类。AZW3/MOBI 格式所有多字节数值均为大端序。
/// </summary>
public static class BigEndian
{
    public static ushort ToUInt16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    public static uint ToUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3]);

    public static int ToInt32(byte[] data, int offset) =>
        (int)ToUInt32(data, offset);

    public static ushort ToUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    public static uint ToUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3]);
}
