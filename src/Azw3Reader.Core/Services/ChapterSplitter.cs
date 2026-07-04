using Azw3Reader.Core.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Azw3Reader.Core.Services;

/// <summary>章节拆分：从完整 HTML 中拆分章节。</summary>
public class ChapterSplitter
{
    // KF8/MOBI 分页标记
    private static readonly Regex PageBreakRegex = new(
        @"<\s*(?:mbp:)?pagebreak[^>]*>|<hr[^>]*class\s*=\s*['""]?pagebreak['""]?[^>]*>",
        RegexOptions.IgnoreCase);

    // 文件位置引用
    private static readonly Regex FileposRegex = new(
        @"<img[^>]*filepos\s*=\s*['""]#?(\d+)['""][^>]*>",
        RegexOptions.IgnoreCase);

    public List<Chapter> Split(string fullHtml)
    {
        if (string.IsNullOrWhiteSpace(fullHtml))
            return [new Chapter { Title = "全文", HtmlContent = fullHtml ?? "", Index = 0 }];

        // 策略 1: 按 pagebreak 分割
        var chapters = SplitByPageBreaks(fullHtml);
        if (chapters.Count >= 2 && chapters.All(c => !string.IsNullOrWhiteSpace(c.HtmlContent)))
            return chapters;

        // 策略 2: 按 nav 标签提取目录
        chapters = ParseNavToc(fullHtml);
        if (chapters.Count > 0) return chapters;

        // 策略 3: 按 h1/h2 标签检测
        chapters = DetectByHeadings(fullHtml);
        if (chapters.Count > 0) return chapters;

        // 保底: 全文一章
        var single = StripOuterTags(fullHtml);
        return [new Chapter { Title = "全文", HtmlContent = single, Index = 0 }];
    }

    private List<Chapter> SplitByPageBreaks(string html)
    {
        var matches = PageBreakRegex.Matches(html);
        if (matches.Count == 0) return [];

        var chapters = new List<Chapter>();
        int lastEnd = 0;
        int idx = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastEnd)
            {
                string content = html[lastEnd..match.Index].Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    // 尝试从内容中提取标题
                    string title = ExtractTitle(content) ?? $"第 {idx + 1} 节";
                    chapters.Add(new Chapter
                    {
                        Title = title,
                        HtmlContent = content,
                        Index = idx++
                    });
                }
            }
            lastEnd = match.Index + match.Length;
        }

        // 剩余内容
        if (lastEnd < html.Length)
        {
            string remaining = html[lastEnd..].Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                string title = ExtractTitle(remaining) ?? $"第 {idx + 1} 节";
                chapters.Add(new Chapter { Title = title, HtmlContent = remaining, Index = idx++ });
            }
        }

        return chapters;
    }

    private string? ExtractTitle(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 从 h1-h3 中提取第一个标题
            for (int level = 1; level <= 3; level++)
            {
                var h = doc.DocumentNode.SelectSingleNode($"//h{level}");
                if (h != null)
                {
                    string text = HtmlEntity.DeEntitize(h.InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length < 100)
                        return text;
                }
            }

            // 从 class=title 或 id=chapter 的元素中提取
            var titleEl = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'title') or contains(@class,'chapter') or @id='chapter']");
            if (titleEl != null)
            {
                string text = HtmlEntity.DeEntitize(titleEl.InnerText).Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length < 100)
                    return text;
            }
        }
        catch { }
        return null;
    }

    private List<Chapter> ParseNavToc(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 查找 <nav epub:type="toc">
            var nav = doc.DocumentNode.SelectSingleNode("//nav[@*[local-name()='type' and .='toc']]")
                   ?? doc.DocumentNode.SelectSingleNode("//nav[contains(@class,'toc')]")
                   ?? doc.DocumentNode.SelectSingleNode("//*[@id='toc']")
                   ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'toc')]");
            if (nav == null) return [];

            var links = nav.SelectNodes(".//a");
            if (links == null || links.Count == 0) return [];

            var result = new List<Chapter>();
            int idx = 0;
            foreach (var link in links)
            {
                string title = HtmlEntity.DeEntitize(link.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length > 100) continue;

                string href = link.GetAttributeValue("href", "") ?? "";
                result.Add(new Chapter
                {
                    Title = title,
                    Anchor = ExtractAnchor(href),
                    Index = idx++
                });
            }
            return result;
        }
        catch { return []; }
    }

    private List<Chapter> DetectByHeadings(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var headings = doc.DocumentNode.SelectNodes("//h1 | //h2 | //h3");
            if (headings == null || headings.Count == 0) return [];

            var result = new List<Chapter>();
            int idx = 0;
            foreach (var h in headings)
            {
                string title = HtmlEntity.DeEntitize(h.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length > 100) continue;

                string? id = h.GetAttributeValue("id", "") ?? h.GetAttributeValue("name", "");
                if (string.IsNullOrEmpty(id)) continue;

                result.Add(new Chapter
                {
                    Title = title,
                    Anchor = $"#{id}",
                    Index = idx++
                });
            }
            return result;
        }
        catch { return []; }
    }

    private static string ExtractAnchor(string href)
    {
        if (string.IsNullOrEmpty(href)) return "";
        int hash = href.IndexOf('#');
        return hash >= 0 ? href[hash..] : "";
    }

    private static string StripOuterTags(string html)
    {
        html = Regex.Replace(html, @"</?(?:html|body|head|meta|title|link)[^>]*>", "",
            RegexOptions.IgnoreCase);
        return html.Trim();
    }
}
