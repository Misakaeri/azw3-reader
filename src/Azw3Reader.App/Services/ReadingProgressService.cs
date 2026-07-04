using System.IO;
using System.Text.Json;

namespace Azw3Reader.App.Services;

public class BookmarkData
{
    public int ChapterIndex { get; set; }
    public double ScrollPercent { get; set; }
    public DateTime LastRead { get; set; }
    public string BookPath { get; set; } = "";
}

/// <summary>阅读进度保存/恢复。</summary>
public class ReadingProgressService
{
    private readonly string _filePath;

    public ReadingProgressService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Azw3Reader");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "progress.json");
    }

    public void Save(string bookPath, int chapterIndex, double scrollPercent)
    {
        var all = LoadAll();
        all[bookPath] = new BookmarkData
        {
            ChapterIndex = chapterIndex,
            ScrollPercent = scrollPercent,
            LastRead = DateTime.Now,
            BookPath = bookPath
        };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(all));
    }

    public BookmarkData? Get(string bookPath)
    {
        var all = LoadAll();
        return all.TryGetValue(bookPath, out var bm) ? bm : null;
    }

    private Dictionary<string, BookmarkData> LoadAll()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, BookmarkData>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
