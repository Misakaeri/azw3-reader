using Azw3Reader.App.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Azw3Reader.App.Services;

/// <summary>标记与笔记的本地持久化 / Markdown 导出。</summary>
public class AnnotationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public AnnotationService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Azw3Reader");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "annotations.json");
    }

    public List<Annotation> GetForBook(string bookPath)
    {
        var book = GetBook(bookPath);
        return book?.Items ?? [];
    }

    public List<Annotation> GetForChapters(string bookPath, int fromChapter, int toChapter)
    {
        return GetForBook(bookPath)
            .Where(a => a.ChapterIndex >= fromChapter && a.ChapterIndex <= toChapter)
            .ToList();
    }

    public Annotation Add(string bookPath, string bookTitle, Annotation annotation)
    {
        var all = LoadAll();
        var book = GetOrCreate(all, bookPath, bookTitle);
        annotation.UpdatedAt = DateTime.Now;
        if (annotation.CreatedAt == default)
            annotation.CreatedAt = DateTime.Now;
        if (string.IsNullOrWhiteSpace(annotation.Id))
            annotation.Id = Guid.NewGuid().ToString("N");

        book.Items.RemoveAll(a => a.Id == annotation.Id);
        book.Items.Add(annotation);
        SaveAll(all);
        return annotation;
    }

    public Annotation? UpdateNote(string bookPath, string id, string note)
    {
        var all = LoadAll();
        var book = GetBook(all, bookPath);
        var item = book?.Items.FirstOrDefault(a => a.Id == id);
        if (item == null) return null;

        item.Note = note ?? "";
        item.UpdatedAt = DateTime.Now;
        SaveAll(all);
        return item;
    }

    public bool Delete(string bookPath, string id)
    {
        var all = LoadAll();
        var book = GetBook(all, bookPath);
        if (book == null) return false;

        int removed = book.Items.RemoveAll(a => a.Id == id);
        if (removed == 0) return false;
        SaveAll(all);
        return true;
    }

    public string ExportMarkdown(string bookPath, string? bookTitle, IReadOnlyList<string>? chapterTitles)
    {
        var items = GetForBook(bookPath)
            .OrderBy(a => a.ChapterIndex)
            .ThenBy(a => a.CreatedAt)
            .ToList();

        string title = string.IsNullOrWhiteSpace(bookTitle)
            ? Path.GetFileNameWithoutExtension(bookPath)
            : bookTitle;

        var sb = new StringBuilder();
        sb.AppendLine($"# {title} - 阅读标注");
        sb.AppendLine();
        sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"标注数量: {items.Count}");
        sb.AppendLine();

        if (items.Count == 0)
        {
            sb.AppendLine("_暂无标注。_");
            return sb.ToString();
        }

        foreach (var a in items)
        {
            string chapterName = chapterTitles != null
                && a.ChapterIndex >= 0
                && a.ChapterIndex < chapterTitles.Count
                ? chapterTitles[a.ChapterIndex]
                : $"第 {a.ChapterIndex + 1} 章";

            string styleLabel = a.Style == AnnotationStyle.Underline ? "划线" : "高亮";
            sb.AppendLine($"## 第 {a.ChapterIndex + 1} 章 · {chapterName}");
            sb.AppendLine();
            sb.AppendLine($"> {EscapeMdQuote(a.SelectedText)}");
            sb.AppendLine();
            sb.AppendLine($"**标记：** {styleLabel}");
            if (string.IsNullOrWhiteSpace(a.Note))
                sb.AppendLine("**笔记：** （无）");
            else
                sb.AppendLine($"**笔记：** {a.Note.Trim()}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeMdQuote(string text)
    {
        return (text ?? "").Replace("\r\n", "\n").Replace("\n", "\n> ").Trim();
    }

    private BookAnnotations? GetBook(string bookPath) => GetBook(LoadAll(), bookPath);

    private static BookAnnotations? GetBook(Dictionary<string, BookAnnotations> all, string bookPath)
    {
        string key = NormalizeKey(bookPath);
        return all.TryGetValue(key, out var book) ? book : null;
    }

    private static BookAnnotations GetOrCreate(
        Dictionary<string, BookAnnotations> all,
        string bookPath,
        string bookTitle)
    {
        string key = NormalizeKey(bookPath);
        if (!all.TryGetValue(key, out var book))
        {
            book = new BookAnnotations { BookPath = bookPath, BookTitle = bookTitle };
            all[key] = book;
        }
        else if (!string.IsNullOrWhiteSpace(bookTitle))
        {
            book.BookTitle = bookTitle;
        }
        return book;
    }

    private static string NormalizeKey(string bookPath) =>
        Path.GetFullPath(bookPath).TrimEnd('\\', '/').ToLowerInvariant();

    private Dictionary<string, BookAnnotations> LoadAll()
    {
        if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, BookAnnotations>>(json, JsonOptions)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveAll(Dictionary<string, BookAnnotations> all)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(all, JsonOptions));
    }
}
