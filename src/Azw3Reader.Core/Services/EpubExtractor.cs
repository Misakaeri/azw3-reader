using Azw3Reader.Core.Models;
using HtmlAgilityPack;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Azw3Reader.Core.Services;

/// <summary>EPUB 解包：ZIP + OPF + spine/nav|ncx → ExtractionResult。</summary>
public class EpubExtractor
{
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace NcxNs = "http://www.daisy.org/z3986/2005/ncx/";

    private static readonly Regex EncryptionHint = new(
        @"encryption\.xml|META-INF/rights\.xml|AdobeADEPT|http://ns\.adobe\.com/adept",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ExtractionResult Extract(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return ExtractFromZip(zip);
    }

    public ExtractionResult ExtractFromZip(ZipArchive zip)
    {
        DetectDrm(zip);

        string opfPath = ResolveOpfPath(zip);
        string opfDir = GetDirectory(opfPath);

        var opfDoc = LoadXml(zip, opfPath)
            ?? throw new InvalidDataException("无法读取 OPF 内容文件。");

        var package = opfDoc.Root
            ?? throw new InvalidDataException("OPF 文件格式无效。");

        var result = new ExtractionResult
        {
            BookInfo = ParseMetadata(package)
        };
        result.BookInfo.FileFormat = "EPUB";

        var manifest = ParseManifest(package, opfDir);
        var spineIds = ParseSpine(package);
        if (spineIds.Count == 0)
            throw new InvalidDataException("EPUB spine 为空，无法提取正文。");

        TrySetCover(zip, package, manifest, result);

        var tocByHref = BuildTocMap(zip, package, manifest, opfDir);

        var htmlBuilder = new StringBuilder();
        var chapters = new List<Chapter>();
        int chapterIndex = 0;

        foreach (string id in spineIds)
        {
            if (!manifest.TryGetValue(id, out var item))
                continue;

            // 跳过 nav 文档本身（避免目录页重复）；封面图页仍保留
            if (item.Properties.Contains("nav", StringComparer.OrdinalIgnoreCase))
                continue;

            if (!IsHtmlMedia(item.MediaType) && !item.Href.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
                && !item.Href.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                && !item.Href.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                continue;

            string? rawHtml = ReadEntryText(zip, item.FullPath);
            if (string.IsNullOrWhiteSpace(rawHtml))
                continue;

            string bodyHtml = ExtractBodyInnerHtml(rawHtml);
            bodyHtml = InlineResources(zip, bodyHtml, GetDirectory(item.FullPath));

            string anchor = $"epub-ch-{chapterIndex}";
            string title = ResolveChapterTitle(tocByHref, item.FullPath, bodyHtml, chapterIndex);

            string section = $"<section id=\"{anchor}\" class=\"epub-chapter\">{bodyHtml}</section>";
            htmlBuilder.AppendLine(section);

            chapters.Add(new Chapter
            {
                Title = title,
                Index = chapterIndex,
                HtmlContent = bodyHtml,
                Anchor = anchor
            });
            chapterIndex++;
        }

        if (chapters.Count == 0)
            throw new InvalidDataException("未能从 EPUB 中提取到可读章节。");

        result.Chapters = chapters;
        result.FullHtml = htmlBuilder.ToString();
        return result;
    }

    private static void DetectDrm(ZipArchive zip)
    {
        var enc = FindEntry(zip, "META-INF/encryption.xml");
        if (enc == null) return;

        using var reader = new StreamReader(enc.Open(), Encoding.UTF8);
        string text = reader.ReadToEnd();
        if (EncryptionHint.IsMatch(text) || text.Contains("EncryptedData", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("此 EPUB 已加密 (DRM)，本阅读器不支持加密文件。请先解除 DRM。");
    }

    private static string ResolveOpfPath(ZipArchive zip)
    {
        var container = FindEntry(zip, "META-INF/container.xml")
            ?? throw new InvalidDataException("不是有效的 EPUB 文件（缺少 META-INF/container.xml）。");

        using var stream = container.Open();
        var doc = XDocument.Load(stream);
        string? fullPath = doc.Root?
            .Element(ContainerNs + "rootfiles")?
            .Elements(ContainerNs + "rootfile")
            .Select(e => (string?)e.Attribute("full-path"))
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

        if (string.IsNullOrWhiteSpace(fullPath))
            throw new InvalidDataException("container.xml 中未找到 OPF 路径。");

        return NormalizeZipPath(fullPath);
    }

    private static BookInfo ParseMetadata(XElement package)
    {
        var meta = package.Element(OpfNs + "metadata") ?? package.Element("metadata");
        var info = new BookInfo();
        if (meta == null) return info;

        info.Title = FirstText(meta, DcNs + "title") ?? FirstText(meta, "title") ?? "";
        info.Author = FirstText(meta, DcNs + "creator") ?? FirstText(meta, "creator") ?? "";
        info.Publisher = FirstText(meta, DcNs + "publisher") ?? FirstText(meta, "publisher") ?? "";
        info.Language = FirstText(meta, DcNs + "language") ?? FirstText(meta, "language") ?? "";
        info.Description = FirstText(meta, DcNs + "description") ?? FirstText(meta, "description") ?? "";

        var identifiers = meta.Elements(DcNs + "identifier").Concat(meta.Elements("identifier"));
        foreach (var id in identifiers)
        {
            string value = (id.Value ?? "").Trim();
            if (value.Contains("isbn", StringComparison.OrdinalIgnoreCase)
                || string.Equals((string?)id.Attribute(OpfNs + "scheme"), "ISBN", StringComparison.OrdinalIgnoreCase)
                || string.Equals((string?)id.Attribute("scheme"), "ISBN", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(value, @"^[\d\-Xx]{10,17}$"))
            {
                info.Isbn = value.Replace("urn:isbn:", "", StringComparison.OrdinalIgnoreCase).Trim();
                break;
            }
        }

        return info;
    }

    private static Dictionary<string, ManifestItem> ParseManifest(XElement package, string opfDir)
    {
        var manifest = package.Element(OpfNs + "manifest") ?? package.Element("manifest");
        var map = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
        if (manifest == null) return map;

        foreach (var item in manifest.Elements(OpfNs + "item").Concat(manifest.Elements("item")))
        {
            string? id = (string?)item.Attribute("id");
            string? href = (string?)item.Attribute("href");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(href))
                continue;

            string mediaType = (string?)item.Attribute("media-type") ?? "";
            string props = (string?)item.Attribute("properties") ?? "";
            string fullPath = NormalizeZipPath(CombineZipPath(opfDir, href));

            map[id] = new ManifestItem
            {
                Id = id,
                Href = href.Replace('\\', '/'),
                FullPath = fullPath,
                MediaType = mediaType,
                Properties = props.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            };
        }

        return map;
    }

    private static List<string> ParseSpine(XElement package)
    {
        var spine = package.Element(OpfNs + "spine") ?? package.Element("spine");
        if (spine == null) return [];

        return spine.Elements(OpfNs + "itemref").Concat(spine.Elements("itemref"))
            .Select(e => (string?)e.Attribute("idref"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    private static void TrySetCover(
        ZipArchive zip,
        XElement package,
        Dictionary<string, ManifestItem> manifest,
        ExtractionResult result)
    {
        string? coverId = null;

        var meta = package.Element(OpfNs + "metadata") ?? package.Element("metadata");
        if (meta != null)
        {
            coverId = meta.Elements(OpfNs + "meta").Concat(meta.Elements("meta"))
                .Where(m => string.Equals((string?)m.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
                .Select(m => (string?)m.Attribute("content"))
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        }

        ManifestItem? coverItem = null;
        if (!string.IsNullOrWhiteSpace(coverId) && manifest.TryGetValue(coverId, out var byId))
            coverItem = byId;
        else
            coverItem = manifest.Values.FirstOrDefault(m =>
                m.Properties.Contains("cover-image", StringComparer.OrdinalIgnoreCase));

        if (coverItem == null) return;

        byte[]? data = ReadEntryBytes(zip, coverItem.FullPath);
        if (data == null || data.Length == 0) return;

        string mime = string.IsNullOrWhiteSpace(coverItem.MediaType)
            ? DetectImageMime(data, coverItem.Href)
            : coverItem.MediaType;

        result.BookInfo.CoverImageUrl = $"data:{mime};base64,{Convert.ToBase64String(data)}";
        result.BookInfo.CoverImageIndex = 0;
        result.Images[0] = new ImageRecord { Data = data, MimeType = mime };
    }

    /// <summary>返回 href(去 fragment) → 标题 的映射，用于给 spine 章节命名。</summary>
    private static Dictionary<string, string> BuildTocMap(
        ZipArchive zip,
        XElement package,
        Dictionary<string, ManifestItem> manifest,
        string opfDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var navItem = manifest.Values.FirstOrDefault(m =>
            m.Properties.Contains("nav", StringComparer.OrdinalIgnoreCase));
        if (navItem != null)
            ParseNavToc(zip, navItem.FullPath, map);

        if (map.Count == 0)
        {
            string? ncxId = (string?)(package.Element(OpfNs + "spine") ?? package.Element("spine"))?.Attribute("toc");
            ManifestItem? ncxItem = null;
            if (!string.IsNullOrWhiteSpace(ncxId) && manifest.TryGetValue(ncxId, out var byId))
                ncxItem = byId;
            else
                ncxItem = manifest.Values.FirstOrDefault(m =>
                    m.MediaType.Equals("application/x-dtbncx+xml", StringComparison.OrdinalIgnoreCase)
                    || m.Href.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));

            if (ncxItem != null)
                ParseNcxToc(zip, ncxItem.FullPath, map);
        }

        return map;
    }

    private static void ParseNavToc(ZipArchive zip, string navPath, Dictionary<string, string> map)
    {
        string? html = ReadEntryText(zip, navPath);
        if (string.IsNullOrWhiteSpace(html)) return;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var navNode = doc.DocumentNode.SelectSingleNode("//*[@epub:type='toc' or @role='doc-toc']")
            ?? doc.DocumentNode.SelectSingleNode("//nav")
            ?? doc.DocumentNode;

        foreach (var a in navNode.SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            string href = HtmlEntity.DeEntitize(a.GetAttributeValue("href", "")).Trim();
            string title = HtmlEntity.DeEntitize(a.InnerText).Trim();
            title = Regex.Replace(title, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(title))
                continue;

            string key = NormalizeHrefKey(ResolveRelative(GetDirectory(navPath), href));
            if (!map.ContainsKey(key))
                map[key] = TruncateTitle(title);
        }
    }

    private static void ParseNcxToc(ZipArchive zip, string ncxPath, Dictionary<string, string> map)
    {
        var doc = LoadXml(zip, ncxPath);
        if (doc?.Root == null) return;

        foreach (var navPoint in doc.Descendants(NcxNs + "navPoint").Concat(doc.Descendants("navPoint")))
        {
            string title = (navPoint.Element(NcxNs + "navLabel") ?? navPoint.Element("navLabel"))?
                .Element(NcxNs + "text")?.Value
                ?? (navPoint.Element(NcxNs + "navLabel") ?? navPoint.Element("navLabel"))?
                .Element("text")?.Value
                ?? "";
            title = Regex.Replace(title.Trim(), @"\s+", " ");

            string? src = (navPoint.Element(NcxNs + "content") ?? navPoint.Element("content"))?
                .Attribute("src")?.Value;
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(title))
                continue;

            string key = NormalizeHrefKey(ResolveRelative(GetDirectory(ncxPath), src));
            if (!map.ContainsKey(key))
                map[key] = TruncateTitle(title);
        }
    }

    private static string ResolveChapterTitle(
        Dictionary<string, string> tocByHref,
        string spineHref,
        string bodyHtml,
        int index)
    {
        string key = NormalizeHrefKey(spineHref);
        if (tocByHref.TryGetValue(key, out var fromToc))
            return fromToc;

        // 无 fragment 精确匹配时，尝试同文件任意 TOC 条目
        string fileOnly = key.Split('#')[0];
        var match = tocByHref.FirstOrDefault(kv =>
            kv.Key.Equals(fileOnly, StringComparison.OrdinalIgnoreCase)
            || kv.Key.StartsWith(fileOnly + "#", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match.Value))
            return match.Value;

        string? heading = ExtractFirstHeading(bodyHtml);
        return heading ?? $"第 {index + 1} 章";
    }

    private static string? ExtractFirstHeading(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            for (int level = 1; level <= 3; level++)
            {
                var h = doc.DocumentNode.SelectSingleNode($"//h{level}");
                if (h == null) continue;
                string text = Regex.Replace(HtmlEntity.DeEntitize(h.InnerText).Trim(), @"\s+", " ");
                if (!string.IsNullOrWhiteSpace(text) && text.Length < 120)
                    return TruncateTitle(text);
            }
        }
        catch { }
        return null;
    }

    private static string ExtractBodyInnerHtml(string rawHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(rawHtml);

        var body = doc.DocumentNode.SelectSingleNode("//body");
        if (body != null)
            return body.InnerHtml;

        // 无 body 时去掉 html/head
        var html = doc.DocumentNode.SelectSingleNode("//html");
        if (html != null)
        {
            foreach (var head in html.SelectNodes("./head") ?? Enumerable.Empty<HtmlNode>())
                head.Remove();
            return html.InnerHtml;
        }

        return rawHtml;
    }

    private static string InlineResources(ZipArchive zip, string html, string baseDir)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // 内联 CSS（link stylesheet → style）
        foreach (var link in doc.DocumentNode.SelectNodes("//link[@rel='stylesheet']")?.ToList()
                 ?? Enumerable.Empty<HtmlNode>())
        {
            string href = link.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                link.Remove();
                continue;
            }

            string cssPath = NormalizeZipPath(ResolveRelative(baseDir, href));
            string? css = ReadEntryText(zip, cssPath);
            if (!string.IsNullOrWhiteSpace(css))
            {
                css = RewriteCssUrls(zip, css, GetDirectory(cssPath));
                var styleNode = doc.CreateElement("style");
                styleNode.InnerHtml = css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase);
                link.ParentNode?.InsertBefore(styleNode, link);
            }
            link.Remove();
        }

        // 图片 → data URI
        foreach (var img in doc.DocumentNode.SelectNodes("//img[@src]")?.ToList()
                 ?? Enumerable.Empty<HtmlNode>())
        {
            string src = img.GetAttributeValue("src", "");
            if (string.IsNullOrWhiteSpace(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            string imgPath = NormalizeZipPath(ResolveRelative(baseDir, src));
            byte[]? data = ReadEntryBytes(zip, imgPath);
            if (data == null) continue;

            string mime = DetectImageMime(data, imgPath);
            img.SetAttributeValue("src", $"data:{mime};base64,{Convert.ToBase64String(data)}");
            img.SetAttributeValue("style", MergeStyle(img.GetAttributeValue("style", ""), "max-width:100%;"));
        }

        // SVG image href
        foreach (var image in doc.DocumentNode.SelectNodes("//*[local-name()='image']")?.ToList()
                 ?? Enumerable.Empty<HtmlNode>())
        {
            string href = image.GetAttributeValue("xlink:href", "");
            if (string.IsNullOrWhiteSpace(href))
                href = image.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            string imgPath = NormalizeZipPath(ResolveRelative(baseDir, href));
            byte[]? data = ReadEntryBytes(zip, imgPath);
            if (data == null) continue;

            string mime = DetectImageMime(data, imgPath);
            string dataUri = $"data:{mime};base64,{Convert.ToBase64String(data)}";
            if (image.Attributes["href"] != null)
                image.SetAttributeValue("href", dataUri);
            if (image.Attributes["xlink:href"] != null)
                image.SetAttributeValue("xlink:href", dataUri);
        }

        return doc.DocumentNode.InnerHtml;
    }

    private static string RewriteCssUrls(ZipArchive zip, string css, string cssDir)
    {
        return Regex.Replace(css, @"url\(\s*['""]?([^'"")]+)['""]?\s*\)", match =>
        {
            string url = match.Groups[1].Value.Trim();
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("#"))
                return match.Value;

            string path = NormalizeZipPath(ResolveRelative(cssDir, url));
            byte[]? data = ReadEntryBytes(zip, path);
            if (data == null) return match.Value;

            string mime = DetectImageMime(data, path);
            if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                mime = "text/css";
            else if (path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
                mime = "font/woff2";
            else if (path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
                mime = "font/woff";
            else if (path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                mime = "font/ttf";
            else if (path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                mime = "font/otf";

            return $"url('data:{mime};base64,{Convert.ToBase64String(data)}')";
        }, RegexOptions.IgnoreCase);
    }

    private static string MergeStyle(string existing, string add)
    {
        if (string.IsNullOrWhiteSpace(existing)) return add;
        if (existing.Contains("max-width", StringComparison.OrdinalIgnoreCase)) return existing;
        return existing.TrimEnd().TrimEnd(';') + ";" + add;
    }

    private static bool IsHtmlMedia(string mediaType) =>
        mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

    private static string? FirstText(XElement parent, XName name) =>
        parent.Elements(name).Select(e => e.Value?.Trim()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? FirstText(XElement parent, string localName) =>
        parent.Elements().Where(e => e.Name.LocalName == localName)
            .Select(e => e.Value?.Trim())
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string TruncateTitle(string title) =>
        title.Length <= 80 ? title : title[..77] + "...";

    private static string NormalizeHrefKey(string href)
    {
        href = href.Replace('\\', '/');
        // 去掉开头 ./
        while (href.StartsWith("./")) href = href[2..];
        return href;
    }

    private static string NormalizeZipPath(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }

    private static string GetDirectory(string path)
    {
        path = path.Replace('\\', '/');
        int idx = path.LastIndexOf('/');
        return idx >= 0 ? path[..idx] : "";
    }

    private static string CombineZipPath(string dir, string relative)
    {
        relative = relative.Replace('\\', '/');
        if (relative.StartsWith('/'))
            return relative.TrimStart('/');
        if (string.IsNullOrEmpty(dir))
            return relative;
        return dir.TrimEnd('/') + "/" + relative;
    }

    private static string ResolveRelative(string baseDir, string href)
    {
        int hash = href.IndexOf('#');
        if (hash >= 0) href = href[..hash];
        return CombineZipPath(baseDir, href);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path)
    {
        path = NormalizeZipPath(path);
        return zip.Entries.FirstOrDefault(e =>
            NormalizeZipPath(e.FullName).Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument? LoadXml(ZipArchive zip, string path)
    {
        var entry = FindEntry(zip, path);
        if (entry == null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static string? ReadEntryText(ZipArchive zip, string path)
    {
        var entry = FindEntry(zip, path);
        if (entry == null) return null;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[]? ReadEntryBytes(ZipArchive zip, string path)
    {
        var entry = FindEntry(zip, path);
        if (entry == null) return null;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string DetectImageMime(byte[] data, string path)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return "image/jpeg";
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return "image/gif";
        if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return "image/webp";
        if (data.Length >= 4 && data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return "image/svg+xml";
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return "image/gif";
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
        return "image/jpeg";
    }

    private sealed class ManifestItem
    {
        public string Id { get; set; } = "";
        public string Href { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string[] Properties { get; set; } = [];
    }
}
