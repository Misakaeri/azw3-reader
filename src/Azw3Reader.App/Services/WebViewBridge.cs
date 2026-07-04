using Azw3Reader.App.Controls;
using System.Runtime.InteropServices;

namespace Azw3Reader.App.Services;

/// <summary>通过 AddHostObjectToScript 暴露给 JS 的桥接对象。</summary>
[ComVisible(true)]
public class WebViewBridge
{
    private readonly BookReaderControl _reader;

    public WebViewBridge(BookReaderControl reader)
    {
        _reader = reader;
    }

    /// <summary>JS 调用：打开上一章。</summary>
    public void PrevChapter() => _reader.Dispatcher.Invoke(() => _reader.PrevChapter());

    /// <summary>JS 调用：打开下一章。</summary>
    public void NextChapter() => _reader.Dispatcher.Invoke(() => _reader.NextChapter());

    /// <summary>JS 调用：跳转到指定章节。</summary>
    public void GoToChapter(int index) => _reader.Dispatcher.Invoke(() => _reader.GoToChapter(index));
}
