using Azw3Reader.Core.Models;
using Azw3Reader.Core.Parsers;
using System.Text;

namespace Azw3Reader.Core.Services;

/// <summary>AZW3 解包核心服务：解析文件、提取 HTML、图片和元数据。</summary>
public class Azw3Extractor
{
    private readonly PdbParser _pdbParser = new();
    private readonly MobiParser _mobiParser = new();
    private readonly PalmDocDecompressor _palmDocDecompressor = new();
    private readonly HuffCdicDecompressor _huffCdicDecompressor = new();

    /// <summary>从 AZW3 文件路径提取完整内容。</summary>
    public ExtractionResult Extract(string filePath)
    {
        byte[] fileData = File.ReadAllBytes(filePath);
        return ExtractFromBytes(fileData, filePath);
    }

    public ExtractionResult ExtractFromBytes(byte[] fileData, string sourceFile = "")
    {
        var result = new ExtractionResult();

        // 1. 解析 PDB 头
        var pdb = _pdbParser.Parse(fileData);
        if (pdb.DbType != "BOOKMOBI")
            throw new InvalidDataException($"不是有效的 AZW3/MOBI 文件 (got: {pdb.DbType})");

        // 2. 解析 Record 0: MOBI 头 + EXTH
        if (pdb.Records.Count == 0)
            throw new InvalidDataException("文件中没有数据记录。");

        byte[] record0 = ReadRecordData(fileData, pdb.Records[0]);
        var (mobiHeader, exth) = _mobiParser.Parse(record0);

        // 3. 检查加密
        if (mobiHeader.EncryptionType != 0)
        {
            result.BookInfo.FileFormat = mobiHeader.EncryptionType == 2 ? "AZW3 (encrypted)" : "MOBI (encrypted)";
            throw new InvalidOperationException("此文件已加密 (DRM)，本阅读器不支持加密文件。请先解除 DRM。");
        }

        // 4. 从 EXTH 提取元数据
        result.BookInfo = BuildBookInfo(exth);
        result.BookInfo.FileFormat = mobiHeader.Kf8SectionOffset > 0 ? "AZW3/KF8" : "MOBI";

        // 5. 提取文本内容和图片
        // 压缩类型: 1=无, 2=PalmDoc(LZ77), 17480(0x4448)=HUFF/CDIC
        bool isNoCompression = mobiHeader.CompressionType == 1;
        bool isPalmDoc = mobiHeader.CompressionType == 2;
        bool isHuffCdic = mobiHeader.CompressionType == 17480;

        if (isNoCompression)
            ExtractRawContent(fileData, pdb, mobiHeader, result);
        else if (isPalmDoc)
            ExtractPalmDocContent(fileData, pdb, mobiHeader, result);
        else if (isHuffCdic)
            ExtractHuffCdicContent(fileData, pdb, mobiHeader, result);
        else
            throw new NotSupportedException($"不支持的压缩类型: {mobiHeader.CompressionType}。");

        // 7. 提取封面图片
        if (result.BookInfo.CoverImageIndex >= 0)
        {
            int coverIdx = result.BookInfo.CoverImageIndex;
            if (result.Images.TryGetValue(coverIdx, out var coverImg))
            {
                result.BookInfo.CoverImageUrl = $"data:{coverImg.MimeType};base64,{Convert.ToBase64String(coverImg.Data)}";
            }
        }

        // 8. 拆分章节
        if (!string.IsNullOrWhiteSpace(result.FullHtml))
        {
            var splitter = new ChapterSplitter();
            result.Chapters = splitter.Split(result.FullHtml);
            // 为无原生锚点的标题注入 id，确保目录跳转生效
            result.FullHtml = splitter.InjectAnchorIds(result.FullHtml, result.Chapters);
        }

        return result;
    }

    private void ExtractPalmDocContent(byte[] fileData, PdbHeader pdb, MobiHeader mobi, ExtractionResult result)
    {
        var textBuffers = new List<byte[]>();
        int imgStart = (int)mobi.FirstImageIndex;

        // 如果没有图片索引信息，尝试从记录总数推断
        if (imgStart <= 0 || imgStart > pdb.Records.Count)
            imgStart = pdb.Records.Count;

        // 提取文本记录 (PalmDoc 压缩)
        for (int i = 0; i < imgStart && i < pdb.Records.Count; i++)
        {
            byte[] raw = ReadRecordData(fileData, pdb.Records[i]);
            // PalmDoc 压缩: 每条记录独立解压
            int uncompLen = (i == 0) ? 0 : (int)mobi.TextLength;
            var decompressed = _palmDocDecompressor.Decompress(raw, uncompLen);
            textBuffers.Add(decompressed);
        }

        // 拼接所有文本记录
        int totalLen = textBuffers.Sum(b => b.Length);
        byte[] allText = new byte[totalLen];
        int offset = 0;
        foreach (var buf in textBuffers)
        {
            Buffer.BlockCopy(buf, 0, allText, offset, buf.Length);
            offset += buf.Length;
        }

        result.FullHtml = DecodeBookText(allText, mobi.TextEncoding);

        // 提取图片
        ExtractImages(fileData, pdb, imgStart, result);
    }

    private void ExtractHuffCdicContent(byte[] fileData, PdbHeader pdb, MobiHeader mobi, ExtractionResult result)
    {
        // KF8 文件: 不依赖可能错误的 FirstImageIndex
        // 遍历所有记录，自动识别 HTML 内容和图片
        var htmlParts = new List<byte[]>();
        var unknownParts = new List<(int index, byte[] raw)>();
        var imageParts = new List<(int index, byte[] raw)>();
        bool foundHtml = false;

        for (int i = 0; i < pdb.Records.Count; i++)
        {
            byte[] raw = ReadRecordData(fileData, pdb.Records[i]);
            if (raw.Length < 4) continue;

            // 1) 检查是否是图片
            if (IsImageData(raw))
            {
                imageParts.Add((i, raw));
                continue;
            }

            // 2) 检查是否包含 HTML 标记
            if (raw.Length >= 10 && ContainsHtmlTag(raw))
            {
                htmlParts.Add(raw);
                foundHtml = true;
                continue;
            }

            // 3) 未知类型
            unknownParts.Add((i, raw));
        }

        byte[]? textData = null;

        // 如果找到了 HTML 内容，拼接使用
        if (foundHtml && htmlParts.Count > 0)
        {
            using var ms = new MemoryStream();
            foreach (var part in htmlParts)
                ms.Write(part, 0, part.Length);
            textData = ms.ToArray();
        }
        else if (unknownParts.Count > 0)
        {
            // 尝试 HUFF/CDIC 解压
            textData = _huffCdicDecompressor.Decompress(fileData, unknownParts, out bool success);
            if (!success || textData == null || textData.Length < 100)
            {
                // 保底: 拼接所有 unknown
                using var ms = new MemoryStream();
                foreach (var (_, raw) in unknownParts)
                    ms.Write(raw, 0, raw.Length);
                textData = ms.ToArray();
            }
        }

        result.FullHtml = DecodeBookText(textData ?? [], mobi.TextEncoding, isKf8: true);

        // 保存图片
        foreach (var (idx, imgData) in imageParts)
        {
            string mime = DetectImageFormat(imgData);
            result.Images[idx] = new ImageRecord { Data = imgData, MimeType = mime };
        }
    }

    /// <summary>检查字节数据是否为图片格式。</summary>
    private static bool IsImageData(byte[] data)
    {
        if (data.Length < 4) return false;
        return (data[0] == 0xFF && data[1] == 0xD8) ||  // JPEG
               (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) || // PNG
               (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) || // GIF
               (data[0] == 0x42 && data[1] == 0x4D); // BMP
    }

    /// <summary>检查记录中是否包含 HTML 标签。</summary>
    private static bool ContainsHtmlTag(byte[] data)
    {
        int searchLen = Math.Min(data.Length, 500);
        // 在数据的前面部分找 <html <head <body <div <p <h1
        for (int i = Math.Min(16, searchLen); i < searchLen - 4; i++)
        {
            if (data[i] == '<')
            {
                char next = char.ToLowerInvariant((char)data[i + 1]);
                if (next == 'h' || next == 'b' || next == 'd' || next == 'p' ||
                    next == '!' || next == '?' || next == '/' ||
                    (next >= 'a' && next <= 'z'))
                    return true;
            }
            // 也检查 UTF-8 BOM
            if (i >= 2 && data[i - 2] == 0xEF && data[i - 1] == 0xBB && data[i] == 0xBF)
                return i + 1 < searchLen;
        }
        return false;
    }

    private void ExtractRawContent(byte[] fileData, PdbHeader pdb, MobiHeader mobi, ExtractionResult result)
    {
        int imgStart = (int)mobi.FirstImageIndex;
        if (imgStart <= 0 || imgStart > pdb.Records.Count)
            imgStart = pdb.Records.Count;

        var textBuffers = new List<byte[]>();
        for (int i = 0; i < imgStart && i < pdb.Records.Count; i++)
        {
            byte[] raw = ReadRecordData(fileData, pdb.Records[i]);
            textBuffers.Add(raw);
        }

        int totalLen = textBuffers.Sum(b => b.Length);
        byte[] allText = new byte[totalLen];
        int offset = 0;
        foreach (var buf in textBuffers)
        { Buffer.BlockCopy(buf, 0, allText, offset, buf.Length); offset += buf.Length; }

        result.FullHtml = DecodeBookText(allText, mobi.TextEncoding);

        ExtractImages(fileData, pdb, imgStart, result);
    }

    private void ExtractImages(byte[] fileData, PdbHeader pdb, int imgStart, ExtractionResult result)
    {
        for (int i = imgStart; i < pdb.Records.Count; i++)
        {
            byte[] raw = ReadRecordData(fileData, pdb.Records[i]);
            if (raw.Length < 4) continue;

            string mime = DetectImageFormat(raw);
            result.Images[i] = new ImageRecord { Data = raw, MimeType = mime };
        }
    }

    private string DetectImageFormat(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return "image/jpeg";
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return "image/gif";
        if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return "image/webp";
        if (data.Length >= 4 && data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
        return "image/jpeg"; // 默认
    }

    private BookInfo BuildBookInfo(List<ExthRecord> exth)
    {
        var info = new BookInfo();
        foreach (var rec in exth)
        {
            switch (rec.Tag)
            {
                case ExthTagType.Title:
                    info.Title = rec.GetString();
                    break;
                case ExthTagType.Author:
                    info.Author = rec.GetString();
                    break;
                case ExthTagType.Publisher:
                    info.Publisher = rec.GetString();
                    break;
                case ExthTagType.Description:
                    info.Description = rec.GetString();
                    break;
                case ExthTagType.Isbn:
                    info.Isbn = rec.GetString();
                    break;
                case ExthTagType.Asin:
                    info.Asin = rec.GetString();
                    break;
                case ExthTagType.Language:
                    info.Language = rec.GetString();
                    break;
                case ExthTagType.CoverIndex:
                    info.CoverImageIndex = (int)rec.GetUInt32();
                    break;
                case ExthTagType.UpdatedAt:
                    if (DateTime.TryParse(rec.GetString(), out var dt))
                        info.UpdatedAt = dt;
                    break;
            }
        }
        return info;
    }

    private static byte[] ReadRecordData(byte[] fileData, PdbRecordEntry entry)
    {
        int len = (int)(entry.EndOffset - entry.Offset);
        if (len <= 0 || entry.Offset + len > fileData.Length)
            return [];

        byte[] result = new byte[len];
        Buffer.BlockCopy(fileData, (int)entry.Offset, result, 0, len);
        return result;
    }

    /// <summary>
    /// 智能编码解码：先按 MOBI 头指定的编码解码，若出现替换字符则退回 GBK/GB18030 尝试。
    /// </summary>
    private static string DecodeBookText(byte[] textData, uint textEncoding, bool isKf8 = false)
    {
        Encoding candidate;
        if (textEncoding == 65001)
            candidate = Encoding.UTF8;
        else if (textEncoding == 1252)
            candidate = Encoding.GetEncoding(1252);
        else if (isKf8 && textEncoding == 2)
            candidate = Encoding.UTF8; // KF8 文件常标为 2，实际是 UTF-8
        else
            candidate = Encoding.UTF8;

        string result = candidate.GetString(textData);

        // 检测前 10000 个字符中的替换字符数量
        int replacementCount = 0;
        int checkLen = Math.Min(result.Length, 10000);
        for (int i = 0; i < checkLen; i++)
        {
            if (result[i] == '�') replacementCount++;
            if (replacementCount > 5) break;
        }

        // 如果替换字符过多（>5），尝试中文编码
        if (replacementCount > 5)
        {
            foreach (int codePage in new[] { 936, 54936 }) // GBK, GB18030
            {
                try
                {
                    var chineseEnc = Encoding.GetEncoding(codePage);
                    string tryResult = chineseEnc.GetString(textData);

                    // 快速判定：前 200 个字符中是否有中文字符
                    int chineseCount = 0;
                    int tryCheckLen = Math.Min(tryResult.Length, 200);
                    for (int i = 0; i < tryCheckLen; i++)
                        if (tryResult[i] >= 0x4E00 && tryResult[i] <= 0x9FFF) chineseCount++;

                    if (chineseCount >= 2)
                        return CleanHtml(tryResult);
                }
                catch { }
            }
        }

        return CleanHtml(result);
    }

    /// <summary>清理 HTML 内容中的前导非 HTML 内容和 null 字符。</summary>
    private static string CleanHtml(string raw)
    {
        int htmlStart = raw.IndexOf('<');
        string cleaned = htmlStart > 0 ? raw[htmlStart..] : raw;
        cleaned = cleaned.Replace("\0", "").Trim();
        return cleaned;
    }
}
