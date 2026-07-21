using Azw3Reader.App.Models;
using Azw3Reader.App.Services;
using Azw3Reader.Core.Models;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Azw3Reader.App.Controls;

[ComVisible(true)]
public partial class BookReaderControl : UserControl
{
    private const int NavigateToStringSafeChars = 800_000;
    private const int ContinuousChunkSafeChars = 700_000;

    private readonly ThemeManager _theme = new();
    private readonly ReadingProgressService _progress = new();
    private readonly AnnotationService _annotations = new();

    private ExtractionResult? _book;
    private WebView2? _bookView;
    private int _currentChapter = 0;
    private int _loadedFrom = 0;
    private int _loadedTo = -1;
    private string _currentFilePath = "";
    private bool _webViewReady = false;
    private bool _useContinuousMode = true;
    private bool _appendBusy = false;
    private string? _tempHtmlPath;

    public event Action<int, double>? ProgressChanged;
    public event Action<int>? ChapterChanged;
    public event Action<Annotation>? AnnotationSelected;

    public BookReaderControl()
    {
        InitializeComponent();
        ThemeCombo.ItemsSource = ThemeManager.ThemeNames;
        ThemeCombo.SelectedIndex = 0;
        Loaded += OnLoaded;
        Unloaded += (_, _) => CleanupTempHtml();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _bookView = new WebView2();
        WebViewContainer.Children.Add(_bookView);

        await _bookView.EnsureCoreWebView2Async();
        _bookView.CoreWebView2.AddHostObjectToScript("bridge", new WebViewBridge(this));
        _bookView.WebMessageReceived += OnWebMessage;
        _bookView.NavigationCompleted += async (_, _) => await RestoreAnnotationsForLoadedRangeAsync();
        await _bookView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetReaderJavaScript());
        _webViewReady = true;

        if (_book != null)
            RenderFromChapter(_currentChapter);
    }

    public void LoadBook(ExtractionResult book, string filePath)
    {
        _book = book;
        _currentFilePath = filePath;
        _currentChapter = 0;
        _loadedFrom = 0;
        _loadedTo = -1;

        _useContinuousMode = book.Chapters.Count > 0
            && book.Chapters.All(c => !string.IsNullOrWhiteSpace(c.HtmlContent));

        var bm = _progress.Get(filePath);
        if (bm != null && bm.ChapterIndex < book.Chapters.Count)
            _currentChapter = bm.ChapterIndex;

        RenderFromChapter(_currentChapter);
    }

    public void GoToChapter(int index) => RenderFromChapter(index);

    public void PrevChapter()
    {
        if (_currentChapter > 0)
            RenderFromChapter(_currentChapter - 1);
    }

    public void NextChapter()
    {
        if (_book != null && _currentChapter < _book.Chapters.Count - 1)
            RenderFromChapter(_currentChapter + 1);
    }

    private void RenderFromChapter(int chapterIndex)
    {
        if (_book == null || _book.Chapters.Count == 0) return;

        chapterIndex = Math.Clamp(chapterIndex, 0, _book.Chapters.Count - 1);
        _currentChapter = chapterIndex;
        UpdateChapterChrome(chapterIndex);
        ChapterChanged?.Invoke(chapterIndex);

        if (!_webViewReady || _bookView == null) return;

        if (!_useContinuousMode)
        {
            RenderLegacySingleDocument(chapterIndex);
            return;
        }

        var (html, from, to) = BuildChapterRangeHtml(chapterIndex);
        _loadedFrom = from;
        _loadedTo = to;

        string page = BuildPage(html, scrollToChapterId: $"ch-{chapterIndex}");
        NavigateToHtml(page);
    }

    private (string html, int from, int to) BuildChapterRangeHtml(int startIndex)
    {
        if (_book == null) return ("", startIndex, startIndex - 1);

        var sb = new StringBuilder();
        int from = startIndex;
        int to = startIndex - 1;

        for (int i = startIndex; i < _book.Chapters.Count; i++)
        {
            string section = BuildChapterSection(i);
            if (sb.Length > 0 && sb.Length + section.Length > ContinuousChunkSafeChars)
                break;

            sb.AppendLine(section);
            to = i;

            if (sb.Length > ContinuousChunkSafeChars)
                break;
        }

        return (sb.ToString(), from, to);
    }

    private string BuildChapterSection(int index)
    {
        var chapter = _book!.Chapters[index];
        string body = ProcessImages((chapter.HtmlContent ?? "").Replace("\0", ""));
        string title = System.Net.WebUtility.HtmlEncode(chapter.Title);
        return $@"<section id=""ch-{index}"" data-chapter=""{index}"" class=""reader-chapter"">
<header class=""chapter-break""><h2 class=""chapter-break-title"">{title}</h2></header>
{body}
</section>";
    }

    private void RenderLegacySingleDocument(int chapterIndex)
    {
        var chapter = _book!.Chapters[chapterIndex];
        string body;
        string? scrollAnchor = null;

        if (!string.IsNullOrWhiteSpace(chapter.HtmlContent))
        {
            body = ProcessImages(chapter.HtmlContent.Replace("\0", ""));
            _loadedFrom = chapterIndex;
            _loadedTo = chapterIndex;
        }
        else
        {
            body = ProcessImages((_book.FullHtml ?? "").Replace("\0", ""));
            scrollAnchor = chapter.Anchor?.TrimStart('#');
            _loadedFrom = 0;
            _loadedTo = Math.Max(0, _book.Chapters.Count - 1);
        }

        string page = BuildPage(body, scrollAnchor: scrollAnchor);
        NavigateToHtml(page);
    }

    private string BuildPage(string bodyHtml, string? scrollAnchor = null, string? scrollToChapterId = null)
    {
        var extraCss = @"
.reader-chapter { margin: 0; padding: 0; }
.chapter-break { margin: 2.5em 0 1.2em; padding-top: 1.2em; border-top: 1px solid rgba(128,128,128,.35); }
.chapter-break:first-child { margin-top: 0; padding-top: 0; border-top: none; }
.chapter-break-title { font-size: 1.15em; font-weight: 600; opacity: .75; margin: 0 0 .8em; }
#azw3-anno-menu {
    position: absolute; z-index: 9999; display: none;
    background: #fff; border: 1px solid #ccc; border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0,0,0,.18); padding: 4px; gap: 2px;
}
#azw3-anno-menu button {
    display: block; width: 100%; border: 0; background: transparent;
    padding: 8px 14px; text-align: left; cursor: pointer; font-size: 13px; border-radius: 6px;
}
#azw3-anno-menu button:hover { background: #f0f4f8; }
";

        string scrollScript = "";
        if (!string.IsNullOrWhiteSpace(scrollToChapterId))
        {
            string safe = JsonSerializer.Serialize(scrollToChapterId);
            scrollScript = $@"
<script>
(function() {{
    var id = {safe};
    setTimeout(function() {{
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({{behavior: 'instant', block: 'start'}});
        else window.scrollTo(0, 0);
    }}, 50);
}})();
</script>";
        }
        else if (!string.IsNullOrWhiteSpace(scrollAnchor))
        {
            string safe = JsonSerializer.Serialize(scrollAnchor);
            scrollScript = $@"
<script>
(function() {{
    var target = {safe};
    setTimeout(function() {{
        var el = document.getElementById(target) || document.querySelector('[name=""' + target + '""]');
        if (el) el.scrollIntoView({{behavior: 'instant', block: 'start'}});
    }}, 100);
}})();
</script>";
        }

        return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/>
<meta name='viewport' content='width=device-width, initial-scale=1.0'/>
<style>{_theme.GetCss()}{extraCss}</style>
</head><body>
{bodyHtml}
<div id=""azw3-anno-menu"">
  <button type=""button"" data-action=""highlight"">高亮</button>
  <button type=""button"" data-action=""underline"">划线</button>
  <button type=""button"" data-action=""cancel"">取消</button>
</div>
{scrollScript}
</body></html>";
    }

    private void NavigateToHtml(string page)
    {
        if (_bookView?.CoreWebView2 == null) return;

        if (page.Length <= NavigateToStringSafeChars)
        {
            try
            {
                _bookView.NavigateToString(page);
                return;
            }
            catch (ArgumentException)
            {
            }
        }

        string dir = Path.Combine(Path.GetTempPath(), "Azw3Reader");
        Directory.CreateDirectory(dir);
        _tempHtmlPath ??= Path.Combine(dir, "current.html");
        File.WriteAllText(_tempHtmlPath, page, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _bookView.CoreWebView2.Navigate(new Uri(_tempHtmlPath).AbsoluteUri);
    }

    private async void AppendForwardChaptersAsync()
    {
        if (_appendBusy || !_useContinuousMode || _book == null || _bookView?.CoreWebView2 == null)
            return;
        if (_loadedTo >= _book.Chapters.Count - 1)
            return;

        _appendBusy = true;
        try
        {
            var sb = new StringBuilder();
            int start = _loadedTo + 1;
            int to = _loadedTo;

            for (int i = start; i < _book.Chapters.Count; i++)
            {
                string section = BuildChapterSection(i);
                if (sb.Length > 0 && sb.Length + section.Length > ContinuousChunkSafeChars)
                    break;
                sb.AppendLine(section);
                to = i;
                if (sb.Length > ContinuousChunkSafeChars)
                    break;
            }

            if (to < start) return;

            string html = sb.ToString();
            string script = $@"
(function() {{
    var html = {JsonSerializer.Serialize(html)};
    document.body.insertAdjacentHTML('beforeend', html);
}})();";
            await _bookView.CoreWebView2.ExecuteScriptAsync(script);
            _loadedTo = to;
            await RestoreAnnotationsForRangeAsync(start, to);
        }
        catch { }
        finally
        {
            _appendBusy = false;
        }
    }

    private async void PrependBackwardChaptersAsync()
    {
        if (_appendBusy || !_useContinuousMode || _book == null || _bookView?.CoreWebView2 == null)
            return;
        if (_loadedFrom <= 0)
            return;

        _appendBusy = true;
        try
        {
            var parts = new List<string>();
            int end = _loadedFrom - 1;
            int from = _loadedFrom;
            int total = 0;

            for (int i = end; i >= 0; i--)
            {
                string section = BuildChapterSection(i);
                if (total > 0 && total + section.Length > ContinuousChunkSafeChars)
                    break;
                parts.Insert(0, section);
                total += section.Length;
                from = i;
                if (total > ContinuousChunkSafeChars)
                    break;
            }

            if (parts.Count == 0) return;

            string html = string.Join("\n", parts);
            string script = $@"
(function() {{
    var html = {JsonSerializer.Serialize(html)};
    var oldH = document.body.scrollHeight;
    document.body.insertAdjacentHTML('afterbegin', html);
    var newH = document.body.scrollHeight;
    window.scrollTo(0, window.scrollY + (newH - oldH));
}})();";
            await _bookView.CoreWebView2.ExecuteScriptAsync(script);
            int oldFrom = _loadedFrom;
            _loadedFrom = from;
            await RestoreAnnotationsForRangeAsync(from, oldFrom - 1);
        }
        catch { }
        finally
        {
            _appendBusy = false;
        }
    }

    private async Task RestoreAnnotationsForLoadedRangeAsync()
    {
        if (_loadedTo < _loadedFrom) return;
        await RestoreAnnotationsForRangeAsync(_loadedFrom, _loadedTo);
    }

    private async Task RestoreAnnotationsForRangeAsync(int from, int to)
    {
        if (_bookView?.CoreWebView2 == null || string.IsNullOrEmpty(_currentFilePath) || to < from)
            return;

        var list = _annotations.GetForChapters(_currentFilePath, from, to);
        if (list.Count == 0) return;

        var payload = list.Select(a => new
        {
            id = a.Id,
            chapterIndex = a.ChapterIndex,
            selectedText = a.SelectedText,
            prefix = a.Prefix,
            suffix = a.Suffix,
            style = a.Style == AnnotationStyle.Underline ? "underline" : "highlight",
            hasNote = !string.IsNullOrWhiteSpace(a.Note)
        });

        string script = $"window.azw3RestoreAnnotations && window.azw3RestoreAnnotations({JsonSerializer.Serialize(payload)});";
        try
        {
            await _bookView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
    }

    private void UpdateChapterChrome(int chapterIndex)
    {
        if (_book == null) return;
        var chapter = _book.Chapters[chapterIndex];
        ChapterTitle.Text = chapter.Title;
        ProgressText.Text = $"{chapterIndex + 1} / {_book.Chapters.Count}";
    }

    private void CleanupTempHtml()
    {
        try
        {
            if (_tempHtmlPath != null && File.Exists(_tempHtmlPath))
                File.Delete(_tempHtmlPath);
        }
        catch { }
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
        return """
(function() {
    if (window.__azw3ReaderBound) return;
    window.__azw3ReaderBound = true;

    function post(obj) {
        try { window.chrome.webview.postMessage(JSON.stringify(obj)); } catch (e) {}
    }

    function getMenu() { return document.getElementById('azw3-anno-menu'); }

    function hideMenu() {
        var m = getMenu();
        if (m) m.style.display = 'none';
    }

    function chapterOfNode(node) {
        var el = node && node.nodeType === 3 ? node.parentElement : node;
        while (el && el !== document.body) {
            if (el.getAttribute && el.hasAttribute('data-chapter')) {
                return parseInt(el.getAttribute('data-chapter'), 10);
            }
            el = el.parentElement;
        }
        return -1;
    }

    function contextAround(range, text) {
        var pre = '', suf = '';
        try {
            var before = range.cloneRange();
            before.selectNodeContents(document.body);
            before.setEnd(range.startContainer, range.startOffset);
            pre = before.toString().slice(-30);
            var after = range.cloneRange();
            after.selectNodeContents(document.body);
            after.setStart(range.endContainer, range.endOffset);
            suf = after.toString().slice(0, 30);
        } catch (e) {}
        return { prefix: pre, suffix: suf, selectedText: text };
    }

    function wrapRange(range, id, style, hasNote) {
        var mark = document.createElement('mark');
        mark.className = style === 'underline' ? 'azw3-ul' : 'azw3-hl';
        if (hasNote) mark.classList.add('has-note');
        mark.setAttribute('data-anno-id', id);
        try {
            range.surroundContents(mark);
            return true;
        } catch (e) {
            try {
                var frag = range.extractContents();
                mark.appendChild(frag);
                range.insertNode(mark);
                return true;
            } catch (e2) {
                return false;
            }
        }
    }

    function findTextInRoot(root, selected, prefix, suffix) {
        if (!root || !selected) return null;
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
        var nodes = [], full = '';
        while (walker.nextNode()) {
            var n = walker.currentNode;
            nodes.push({ node: n, start: full.length });
            full += n.nodeValue || '';
        }
        var needle = (prefix || '') + selected + (suffix || '');
        var idx = needle ? full.indexOf(needle) : -1;
        var start = -1, end = -1;
        if (idx >= 0) {
            start = idx + (prefix || '').length;
            end = start + selected.length;
        } else {
            idx = full.indexOf(selected);
            if (idx < 0) return null;
            start = idx;
            end = idx + selected.length;
        }
        function pointAt(pos) {
            for (var i = 0; i < nodes.length; i++) {
                var len = (nodes[i].node.nodeValue || '').length;
                if (pos <= nodes[i].start + len) {
                    return { node: nodes[i].node, offset: pos - nodes[i].start };
                }
            }
            var last = nodes[nodes.length - 1];
            return { node: last.node, offset: (last.node.nodeValue || '').length };
        }
        var s = pointAt(start), e = pointAt(end);
        var range = document.createRange();
        range.setStart(s.node, Math.max(0, s.offset));
        range.setEnd(e.node, Math.max(0, e.offset));
        return range;
    }

    window.azw3RestoreAnnotations = function(list) {
        if (!list || !list.length) return;
        for (var i = 0; i < list.length; i++) {
            var a = list[i];
            if (document.querySelector('mark[data-anno-id="' + a.id + '"]')) continue;
            var root = document.getElementById('ch-' + a.chapterIndex) || document.body;
            var range = findTextInRoot(root, a.selectedText, a.prefix, a.suffix);
            if (!range) continue;
            wrapRange(range, a.id, a.style, !!a.hasNote);
        }
    };

    function unwrapMark(mark) {
        if (!mark || !mark.parentNode) return;
        var id = mark.getAttribute('data-anno-id') || '';
        var parent = mark.parentNode;
        while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
        parent.removeChild(mark);
        parent.normalize();
        return id;
    }

    function markCoveringRange(range) {
        if (!range) return null;
        var startEl = range.startContainer.nodeType === 3 ? range.startContainer.parentElement : range.startContainer;
        var endEl = range.endContainer.nodeType === 3 ? range.endContainer.parentElement : range.endContainer;
        var m1 = startEl && startEl.closest ? startEl.closest('mark[data-anno-id]') : null;
        var m2 = endEl && endEl.closest ? endEl.closest('mark[data-anno-id]') : null;
        if (m1 && m1 === m2) return m1;
        // 选区文本与某个 mark 文本完全一致时也视为同一标记
        var text = range.toString().replace(/\s+/g, ' ').trim();
        if (!text) return null;
        var marks = document.querySelectorAll('mark[data-anno-id]');
        for (var i = 0; i < marks.length; i++) {
            var t = (marks[i].textContent || '').replace(/\s+/g, ' ').trim();
            if (t === text) return marks[i];
        }
        return null;
    }

    function markStyleOf(mark) {
        if (!mark) return '';
        if (mark.classList.contains('azw3-ul')) return 'underline';
        return 'highlight';
    }

    window.azw3UpdateAnnotationMark = function(id, hasNote, remove) {
        var el = document.querySelector('mark[data-anno-id="' + id + '"]');
        if (!el) return;
        if (remove) {
            unwrapMark(el);
            return;
        }
        if (hasNote) el.classList.add('has-note');
        else el.classList.remove('has-note');
    };

    var pendingRange = null;
    document.addEventListener('mouseup', function(e) {
        if (e.target && e.target.closest && e.target.closest('#azw3-anno-menu')) return;
        setTimeout(function() {
            var sel = window.getSelection();
            var menu = getMenu();
            if (!sel || sel.isCollapsed || !sel.rangeCount || !menu) {
                hideMenu();
                return;
            }
            var text = sel.toString().replace(/\s+/g, ' ').trim();
            if (!text || text.length < 1) { hideMenu(); return; }
            var range = sel.getRangeAt(0);
            var c1 = chapterOfNode(range.startContainer);
            var c2 = chapterOfNode(range.endContainer);
            if (c1 < 0 || c1 !== c2) {
                hideMenu();
                return;
            }
            pendingRange = range.cloneRange();
            menu.dataset.chapter = String(c1);
            var rect = range.getBoundingClientRect();
            menu.style.display = 'block';
            menu.style.left = Math.max(8, window.scrollX + rect.left) + 'px';
            menu.style.top = Math.max(8, window.scrollY + rect.bottom + 6) + 'px';
        }, 10);
    });

    document.addEventListener('mousedown', function(e) {
        var menu = getMenu();
        if (menu && e.target && (!e.target.closest || !e.target.closest('#azw3-anno-menu'))) {
            if (!(e.target.closest && e.target.closest('mark[data-anno-id]')))
                hideMenu();
        }
    });

    document.addEventListener('click', function(e) {
        var menu = getMenu();
        if (menu && e.target && e.target.closest && e.target.closest('#azw3-anno-menu')) {
            e.preventDefault();
            e.stopPropagation();
            var btn = e.target.closest('button');
            if (!btn) return;
            var action = btn.getAttribute('data-action');
            hideMenu();
            if (action === 'cancel' || !pendingRange) { pendingRange = null; return; }
            var style = action === 'underline' ? 'underline' : 'highlight';
            var chapterIndex = parseInt(menu.dataset.chapter || '-1', 10);

            // 再点一次同风格 = 取消标记
            var existing = markCoveringRange(pendingRange);
            if (existing) {
                var oldStyle = markStyleOf(existing);
                var oldId = existing.getAttribute('data-anno-id') || '';
                if (oldStyle === style) {
                    unwrapMark(existing);
                    pendingRange = null;
                    window.getSelection().removeAllRanges();
                    post({ type: 'annotation-delete', id: oldId });
                    return;
                }
                // 换风格：去掉旧标记再新建
                unwrapMark(existing);
                post({ type: 'annotation-delete', id: oldId });
            }

            var ctx = contextAround(pendingRange, pendingRange.toString().replace(/\s+/g, ' ').trim());
            var id = 'tmp_' + Date.now().toString(36);
            if (!wrapRange(pendingRange, id, style, false)) {
                pendingRange = null;
                post({type:'annotation-error', message:'无法标记此选区（可能跨了复杂标签）'});
                return;
            }
            pendingRange = null;
            window.getSelection().removeAllRanges();
            post({
                type: 'annotation-create',
                id: id,
                chapterIndex: chapterIndex,
                selectedText: ctx.selectedText,
                prefix: ctx.prefix,
                suffix: ctx.suffix,
                style: style
            });
            return;
        }

        var mark = e.target && e.target.closest && e.target.closest('mark[data-anno-id]');
        if (mark) {
            e.preventDefault();
            e.stopPropagation();
            post({ type: 'annotation-click', id: mark.getAttribute('data-anno-id') });
            return;
        }

        var sel = window.getSelection();
        if (sel && !sel.isCollapsed) return;

        var w = window.innerWidth;
        var x = e.clientX;
        var ratio = x / w;
        if (ratio < 0.33) post({type: 'prev'});
        else if (ratio > 0.66) post({type: 'next'});
    }, true);

    document.addEventListener('keydown', function(e) {
        if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
            post({type: 'prev'});
        } else if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') {
            e.preventDefault();
            post({type: 'next'});
        } else if (e.key === 'Escape') {
            hideMenu();
        }
    });

    var scrollTimer;
    var edgeCooldown = false;
    function withCooldown(fn) {
        if (edgeCooldown) return;
        edgeCooldown = true;
        fn();
        setTimeout(function() { edgeCooldown = false; }, 600);
    }

    function checkEdges(deltaY) {
        var maxScroll = document.body.scrollHeight - window.innerHeight;
        if (maxScroll <= 4) {
            if (deltaY > 0) withCooldown(function() { post({type: 'need-more'}); });
            else if (deltaY < 0) withCooldown(function() { post({type: 'need-prev'}); });
            return;
        }
        if (window.scrollY + window.innerHeight >= document.body.scrollHeight - 24 && deltaY > 0) {
            withCooldown(function() { post({type: 'need-more'}); });
        } else if (window.scrollY <= 8 && deltaY < 0) {
            withCooldown(function() { post({type: 'need-prev'}); });
        }
    }

    document.addEventListener('wheel', function(e) {
        checkEdges(e.deltaY);
    }, { passive: true });

    document.addEventListener('scroll', function() {
        hideMenu();
        clearTimeout(scrollTimer);
        scrollTimer = setTimeout(function() {
            var h = document.body.scrollHeight - window.innerHeight;
            var pct = h > 0 ? (window.scrollY / h) : 0;
            post({type: 'progress', value: pct});

            var sections = document.querySelectorAll('section[data-chapter]');
            if (!sections.length) return;
            var best = null;
            var bestTop = -Infinity;
            for (var i = 0; i < sections.length; i++) {
                var rect = sections[i].getBoundingClientRect();
                if (rect.top <= 120 && rect.top > bestTop) {
                    bestTop = rect.top;
                    best = sections[i];
                }
            }
            if (!best && sections[0]) best = sections[0];
            if (best) {
                var idx = parseInt(best.getAttribute('data-chapter'), 10);
                if (!isNaN(idx)) post({type: 'chapter-visible', index: idx});
            }
        }, 120);
    });
})();
""";
    }

    private void OnWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string raw = e.TryGetWebMessageAsString() ?? e.WebMessageAsJson;
            if (raw.Length >= 2 && raw[0] == '"')
                raw = JsonSerializer.Deserialize<string>(raw) ?? raw;

            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";

            switch (type)
            {
                case "prev":
                    Dispatcher.Invoke(PrevChapter);
                    break;
                case "next":
                    Dispatcher.Invoke(NextChapter);
                    break;
                case "need-more":
                    Dispatcher.Invoke(AppendForwardChaptersAsync);
                    break;
                case "need-prev":
                    Dispatcher.Invoke(PrependBackwardChaptersAsync);
                    break;
                case "progress":
                    double pct = root.GetProperty("value").GetDouble();
                    Dispatcher.Invoke(() => OnScrollProgress(pct));
                    break;
                case "chapter-visible":
                    int idx = root.GetProperty("index").GetInt32();
                    Dispatcher.Invoke(() => OnChapterVisible(idx));
                    break;
                case "annotation-create":
                    Dispatcher.Invoke(() => OnAnnotationCreate(root));
                    break;
                case "annotation-click":
                    string clickId = root.GetProperty("id").GetString() ?? "";
                    Dispatcher.Invoke(() => OnAnnotationClick(clickId));
                    break;
                case "annotation-delete":
                    string delId = root.GetProperty("id").GetString() ?? "";
                    Dispatcher.Invoke(() => OnAnnotationDeleteFromToggle(delId));
                    break;
                case "annotation-error":
                    string msg = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "") : "标记失败";
                    Dispatcher.Invoke(() => MessageBox.Show(msg, "标记", MessageBoxButton.OK, MessageBoxImage.Warning));
                    break;
            }
        }
        catch { }
    }

    private async void OnAnnotationCreate(JsonElement root)
    {
        if (string.IsNullOrEmpty(_currentFilePath) || _book == null) return;

        string tempId = root.GetProperty("id").GetString() ?? "";
        string styleStr = root.GetProperty("style").GetString() ?? "highlight";
        var annotation = new Annotation
        {
            ChapterIndex = root.GetProperty("chapterIndex").GetInt32(),
            SelectedText = root.GetProperty("selectedText").GetString() ?? "",
            Prefix = root.TryGetProperty("prefix", out var p) ? (p.GetString() ?? "") : "",
            Suffix = root.TryGetProperty("suffix", out var s) ? (s.GetString() ?? "") : "",
            Style = styleStr == "underline" ? AnnotationStyle.Underline : AnnotationStyle.Highlight
        };

        if (string.IsNullOrWhiteSpace(annotation.SelectedText) || annotation.ChapterIndex < 0)
            return;

        var saved = _annotations.Add(_currentFilePath, _book.BookInfo.Title, annotation);

        // 把临时 id 换成持久 id
        if (_bookView?.CoreWebView2 != null && !string.IsNullOrEmpty(tempId))
        {
            string script = $@"
(function() {{
    var el = document.querySelector('mark[data-anno-id={JsonSerializer.Serialize(tempId)}]');
    if (el) el.setAttribute('data-anno-id', {JsonSerializer.Serialize(saved.Id)});
}})();";
            try { await _bookView.CoreWebView2.ExecuteScriptAsync(script); } catch { }
        }
    }

    private void OnAnnotationDeleteFromToggle(string id)
    {
        if (string.IsNullOrEmpty(_currentFilePath) || string.IsNullOrEmpty(id)) return;
        // 临时 id（尚未入库）忽略
        if (id.StartsWith("tmp_", StringComparison.Ordinal)) return;
        _annotations.Delete(_currentFilePath, id);
    }

    private void OnAnnotationClick(string id)
    {
        if (string.IsNullOrEmpty(_currentFilePath) || string.IsNullOrEmpty(id)) return;

        var item = _annotations.GetForBook(_currentFilePath).FirstOrDefault(a => a.Id == id);
        if (item == null) return;

        AnnotationSelected?.Invoke(item);
    }

    public void NotifyAnnotationNoteChanged(string id, bool hasNote)
    {
        _ = UpdateMarkInViewAsync(id, hasNote, remove: false);
    }

    public Task RemoveAnnotationMarkAsync(string id) =>
        UpdateMarkInViewAsync(id, hasNote: false, remove: true);

    private async Task UpdateMarkInViewAsync(string id, bool hasNote, bool remove)
    {
        if (_bookView?.CoreWebView2 == null) return;
        string script =
            $"window.azw3UpdateAnnotationMark && window.azw3UpdateAnnotationMark({JsonSerializer.Serialize(id)}, {(hasNote ? "true" : "false")}, {(remove ? "true" : "false")});";
        try { await _bookView.CoreWebView2.ExecuteScriptAsync(script); } catch { }
    }

    private void OnExportAnnotations(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath) || _book == null)
        {
            MessageBox.Show("请先打开一本书。", "导出标注", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var titles = _book.Chapters.Select(c => c.Title).ToList();
        string md = _annotations.ExportMarkdown(_currentFilePath, _book.BookInfo.Title, titles);
        string defaultName = $"{SanitizeFileName(_book.BookInfo.Title)}-标注.md";
        if (string.IsNullOrWhiteSpace(_book.BookInfo.Title))
            defaultName = Path.GetFileNameWithoutExtension(_currentFilePath) + "-标注.md";

        var dialog = new SaveFileDialog
        {
            Title = "导出标注为 Markdown",
            Filter = "Markdown|*.md|所有文件|*.*",
            FileName = defaultName
        };

        if (dialog.ShowDialog() != true) return;

        File.WriteAllText(dialog.FileName, md, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show($"已导出到:\n{dialog.FileName}", "导出标注", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "book";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private void OnChapterVisible(int index)
    {
        if (_book == null || index < 0 || index >= _book.Chapters.Count) return;
        if (index == _currentChapter) return;

        _currentChapter = index;
        UpdateChapterChrome(index);
        ChapterChanged?.Invoke(index);
        if (!string.IsNullOrEmpty(_currentFilePath))
            _progress.Save(_currentFilePath, _currentChapter, 0);
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
        if (_theme.FontSize > 12) { _theme.FontSize -= 2; RenderFromChapter(_currentChapter); }
    }

    private void OnFontLarge(object sender, RoutedEventArgs e)
    {
        if (_theme.FontSize < 36) { _theme.FontSize += 2; RenderFromChapter(_currentChapter); }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        _theme.CurrentTheme = (ThemeType)ThemeCombo.SelectedIndex;
        RenderFromChapter(_currentChapter);
    }
}
