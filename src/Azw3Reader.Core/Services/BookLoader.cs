using Azw3Reader.Core.Models;

namespace Azw3Reader.Core.Services;

/// <summary>按扩展名分发到对应格式提取器。</summary>
public static class BookLoader
{
    private static readonly HashSet<string> KindleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".azw3", ".mobi", ".azw", ".kf8"
    };

    public static ExtractionResult Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("文件不存在。", filePath);

        string ext = Path.GetExtension(filePath);
        if (ext.Equals(".epub", StringComparison.OrdinalIgnoreCase))
            return new EpubExtractor().Extract(filePath);

        if (KindleExtensions.Contains(ext))
            return new Azw3Extractor().Extract(filePath);

        throw new NotSupportedException(
            $"不支持的文件格式: {ext}。支持 .epub / .azw3 / .mobi / .azw / .kf8。");
    }
}
