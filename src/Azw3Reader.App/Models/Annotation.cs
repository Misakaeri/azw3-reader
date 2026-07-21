namespace Azw3Reader.App.Models;

public enum AnnotationStyle
{
    Highlight = 0,
    Underline = 1
}

public class Annotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ChapterIndex { get; set; }
    public string SelectedText { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public AnnotationStyle Style { get; set; } = AnnotationStyle.Highlight;
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class BookAnnotations
{
    public string BookPath { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public List<Annotation> Items { get; set; } = [];
}
