using Azw3Reader.App.Services;
using Azw3Reader.Core.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Azw3Reader.App.Controls;

[ComVisible(true)]
public partial class BookReaderControl : UserControl
{
    private readonly ThemeManager _theme = new();
    private readonly ReadingProgressService _progress = new();

    private ExtractionResult? _book;
    private int _currentChapter = 0;
    private string _currentFilePath = "";
    private string _processedFullHtml = "";
    private bool _webViewReady = false;

    public event Action<int, double>? ProgressChanged;

    public BookReaderControl()
    {
        InitializeComponent();
        ThemeCombo.ItemsSource = ThemeManager.ThemeNames;
        ThemeCombo.SelectedIndex = 0;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await BookView.EnsureCoreWebView2Async();
        BookView.CoreWebView2.AddHostObjectToScript("bridge", new WebViewBridge(this));
        BookView.WebMessageReceived += OnWebMessage;
        await BookView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetReaderJavaScript());
        _webViewReady = true;

        // 如果有待加载的书，完成加载
        if (_book != null)
            RenderChapter(_currentChapter);
    }

    public void LoadBook(ExtractionResult book, string filePath)
    {
        _book = book;
        _currentFilePath = filePath;
        _currentChapter = 0;

        // 预处理完整 HTML（图片替换 + 清理）
        _processedFullHtml = ProcessImages(book.FullHtml);
        _processedFullHtml = _processedFullHtml.Replace("\0", "");

        // 尝试恢复阅读进度
        var bm = _progress.Get(filePath);
        if (bm != null && bm.ChapterIndex < book.Chapters.Count)
            _currentChapter = bm.ChapterIndex;

        RenderChapter(_currentChapter);
    }

    private void RenderChapter(int chapterIndex)
    {
        if (_book == null || _book.Chapters.Count == 0) return;

        chapterIndex = Math.Clamp(chapterIndex, 0, _book.Chapters.Count - 1);
        _currentChapter = chapterIndex;
        var chapter = _book.Chapters[chapterIndex];

        ChapterTitle.Text = chapter.Title;
        ProgressText.Text = $"{chapterIndex + 1} / {_book.Chapters.Count}";

        // 首次加载或切换主题/字体时构建完整页面
        if (!_webViewReady) return;

        string page = BuildFullPage();
        BookView.NavigateToString(page);
    }

    private string BuildFullPage()
    {
        return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/>
<meta name='viewport' content='width=device-width, initial-scale=1.0'/>
<style>{_theme.GetCss()}</style>
</head><body>
{_processedFullHtml}
<script>
(function() {{
    // 页面加载完成后滚动到当前章节位置
    var chapters = {GetChapterAnchorsJson()};
    var target = chapters[{_currentChapter}];
    if (target) {{
        setTimeout(function() {{
            var el = document.getElementById(target) || document.querySelector('[name=""' + target + '""]');
            if (el) el.scrollIntoView({{behavior: 'instant', block: 'start'}});
        }}, 100);
    }}
}})();
</script>
</body></html>";
    }

    private string GetChapterAnchorsJson()
    {
        if (_book == null) return "[]";
        var anchors = _book.Chapters.Select(c => c.Anchor?.TrimStart('#') ?? "").ToArray();
        return System.Text.Json.JsonSerializer.Serialize(anchors);
    }

    private string ProcessImages(string html)
    {
        if (_book == null) return html;

        return System.Text.RegularExpressions.Regex.Replace(html,
            @"<img[^>]*filepos\s*=\s*['""]#?(\d+)['""][^>]*>",
            match =>
            {
                int idx = int.Parse(match.Groups[1].Value);
                if (_book.Images.TryGetValue(idx, out var img))
                {
                    string b64 = Convert.ToBase64String(img.Data);
                    return $"<img src='data:{img.MimeType};base64,{b64}' style='max-width:100%;' />";
                }
                return match.Value;
            });
    }

    private string GetReaderJavaScript()
    {
        return @"
(function() {
    document.addEventListener('click', function(e) {
        var w = window.innerWidth;
        var x = e.clientX;
        var ratio = x / w;
        if (ratio < 0.33) {
            window.chrome.webview.postMessage(JSON.stringify({type: 'prev'}));
        } else if (ratio > 0.66) {
            window.chrome.webview.postMessage(JSON.stringify({type: 'next'}));
        }
    });

    document.addEventListener('keydown', function(e) {
        if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
            window.chrome.webview.postMessage(JSON.stringify({type: 'prev'}));
        } else if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') {
            e.preventDefault();
            window.chrome.webview.postMessage(JSON.stringify({type: 'next'}));
        }
    });

    var scrollTimer;
    document.addEventListener('scroll', function() {
        clearTimeout(scrollTimer);
        scrollTimer = setTimeout(function() {
            var h = document.body.scrollHeight - window.innerHeight;
            var pct = h > 0 ? (window.scrollY / h) : 0;
            window.chrome.webview.postMessage(JSON.stringify({type: 'progress', value: pct}));
        }, 300);
    });
})();";
    }

    private void OnWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            var root = json.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";

            switch (type)
            {
                case "prev":
                    Dispatcher.Invoke(() => PrevChapter());
                    break;
                case "next":
                    Dispatcher.Invoke(() => NextChapter());
                    break;
                case "progress":
                    double pct = root.GetProperty("value").GetDouble();
                    Dispatcher.Invoke(() => OnScrollProgress(pct));
                    break;
            }
        }
        catch { }
    }

    public void PrevChapter()
    {
        if (_currentChapter > 0)
            RenderChapter(_currentChapter - 1);
    }

    public void NextChapter()
    {
        if (_book != null && _currentChapter < _book.Chapters.Count - 1)
            RenderChapter(_currentChapter + 1);
    }

    private void OnScrollProgress(double percent)
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            _progress.Save(_currentFilePath, _currentChapter, percent);
            ProgressChanged?.Invoke(_currentChapter, percent);
        }
    }

    private void OnFontSmall(object sender, RoutedEventArgs e)
    {
        if (_theme.FontSize > 12) { _theme.FontSize -= 2; RenderChapter(_currentChapter); }
    }

    private void OnFontLarge(object sender, RoutedEventArgs e)
    {
        if (_theme.FontSize < 36) { _theme.FontSize += 2; RenderChapter(_currentChapter); }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        _theme.CurrentTheme = (ThemeType)ThemeCombo.SelectedIndex;
        RenderChapter(_currentChapter);
    }

    public void GoToChapter(int index)
    {
        RenderChapter(index);
    }
}
