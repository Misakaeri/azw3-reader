using Azw3Reader.App.Controls;
using Azw3Reader.App.Models;
using Azw3Reader.App.Services;
using Azw3Reader.App.ViewModels;
using Azw3Reader.Core.Models;
using Azw3Reader.Core.Services;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Azw3Reader.App;

public partial class MainWindow : Window
{
    private readonly ReaderViewModel _vm = new();
    private readonly AnnotationService _annotations = new();
    private readonly DispatcherTimer _noteSaveTimer;

    private ExtractionResult? _currentBook;
    private string _currentFilePath = "";
    private Annotation? _activeAnnotation;
    private bool _syncingChapterList;
    private bool _syncingNoteBox;
    private bool _leftCollapsed;
    private bool _rightCollapsed = true;

    private GridLength _leftPanelWidth = new(240);
    private GridLength _rightPanelWidth = new(320);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        AllowDrop = true;
        Drop += OnDropFile;

        ReaderControl.ChapterChanged += OnReaderChapterChanged;
        ReaderControl.AnnotationSelected += OnAnnotationSelected;

        _noteSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _noteSaveTimer.Tick += (_, _) =>
        {
            _noteSaveTimer.Stop();
            SaveActiveNote();
        };

        ApplyLeftCollapsed(false);
        ApplyRightCollapsed(true);
    }

    private void OnReaderChapterChanged(int chapterIndex)
    {
        if (_currentBook == null || chapterIndex < 0 || chapterIndex >= _currentBook.Chapters.Count)
            return;

        _syncingChapterList = true;
        try
        {
            ChapterList.SelectedIndex = chapterIndex;
            ChapterList.ScrollIntoView(_currentBook.Chapters[chapterIndex]);
        }
        finally
        {
            _syncingChapterList = false;
        }
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "电子书|*.epub;*.azw3;*.mobi;*.azw;*.kf8|EPUB|*.epub|AZW3/MOBI|*.azw3;*.mobi;*.azw;*.kf8|所有文件|*.*",
            Title = "选择一本书籍"
        };

        if (dialog.ShowDialog() == true)
            LoadBook(dialog.FileName);
    }

    private void OnDropFile(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
                LoadBook(files[0]);
        }
    }

    private void LoadBook(string filePath)
    {
        try
        {
            CloseNotePanel();
            StatusBar.Text = "正在解析...";
            _currentBook = BookLoader.Load(filePath);
            _currentFilePath = filePath;

            BookTitle.Text = _currentBook.BookInfo.Title;
            BookAuthor.Text = _currentBook.BookInfo.Author;
            FileFormatText.Text = _currentBook.BookInfo.FileFormat;
            ChapterList.ItemsSource = _currentBook.Chapters;

            ReaderControl.LoadBook(_currentBook, filePath);

            StatusBar.Text = $"已加载: {System.IO.Path.GetFileName(filePath)}";
            Title = $"AZW3 阅读器 - {_currentBook.BookInfo.Title}";
        }
        catch (InvalidOperationException ex)
        {
            StatusBar.Text = "错误";
            MessageBox.Show(ex.Message, "解析失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (NotSupportedException ex)
        {
            StatusBar.Text = "不支持的格式";
            MessageBox.Show(ex.Message, "格式不支持", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusBar.Text = "加载失败";
            MessageBox.Show($"无法打开文件:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnChapterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingChapterList) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is Chapter chapter)
            ReaderControl.GoToChapter(chapter.Index);
    }

    // —— 左右栏折叠 ——

    private void OnToggleLeft(object sender, RoutedEventArgs e) =>
        ApplyLeftCollapsed(!_leftCollapsed);

    private void OnToggleRight(object sender, RoutedEventArgs e)
    {
        if (_rightCollapsed && _activeAnnotation == null)
        {
            StatusBar.Text = "点击正文中的标记以打开笔记";
            return;
        }
        ApplyRightCollapsed(!_rightCollapsed);
    }

    private void ApplyLeftCollapsed(bool collapsed)
    {
        _leftCollapsed = collapsed;
        if (collapsed)
        {
            if (LeftPanelCol.Width.Value > 40)
                _leftPanelWidth = LeftPanelCol.Width;
            LeftPanelCol.Width = new GridLength(0);
            LeftPanelCol.MinWidth = 0;
            LeftSplitCol.Width = new GridLength(0);
            LeftPanel.Visibility = Visibility.Collapsed;
            LeftSplitter.Visibility = Visibility.Collapsed;
            ToggleLeftBtn.Content = "›";
            ToggleLeftBtn.ToolTip = "展开目录";
        }
        else
        {
            LeftPanelCol.Width = _leftPanelWidth.Value > 0 ? _leftPanelWidth : new GridLength(240);
            LeftPanelCol.MinWidth = 160;
            LeftSplitCol.Width = new GridLength(3);
            LeftPanel.Visibility = Visibility.Visible;
            LeftSplitter.Visibility = Visibility.Visible;
            ToggleLeftBtn.Content = "‹";
            ToggleLeftBtn.ToolTip = "折叠目录";
        }
    }

    private void ApplyRightCollapsed(bool collapsed)
    {
        _rightCollapsed = collapsed;
        if (collapsed)
        {
            if (RightPanelCol.Width.Value > 40)
                _rightPanelWidth = RightPanelCol.Width;
            RightPanelCol.Width = new GridLength(0);
            RightPanelCol.MinWidth = 0;
            RightSplitCol.Width = new GridLength(0);
            RightPanel.Visibility = Visibility.Collapsed;
            RightSplitter.Visibility = Visibility.Collapsed;
            ToggleRightBtn.Content = "‹";
            ToggleRightBtn.ToolTip = "展开笔记";
        }
        else
        {
            RightPanelCol.Width = _rightPanelWidth.Value > 0 ? _rightPanelWidth : new GridLength(320);
            RightPanelCol.MinWidth = 220;
            RightSplitCol.Width = new GridLength(3);
            RightPanel.Visibility = Visibility.Visible;
            RightSplitter.Visibility = Visibility.Visible;
            ToggleRightBtn.Content = "›";
            ToggleRightBtn.ToolTip = "折叠笔记";
        }
    }

    // —— 笔记栏 ——

    private void OnAnnotationSelected(Annotation annotation)
    {
        SaveActiveNote();
        _activeAnnotation = annotation;
        NoteTitleText.Text = $"笔记 {DateTime.Now:HH:mm}";
        NoteExcerptText.Text = annotation.SelectedText;
        _syncingNoteBox = true;
        try
        {
            NoteBox.Text = annotation.Note ?? "";
        }
        finally
        {
            _syncingNoteBox = false;
        }
        UpdateNotePlaceholder();
        ApplyRightCollapsed(false);
        Dispatcher.BeginInvoke(() =>
        {
            AdjustNoteBoxHeight();
            NoteBox.Focus();
            NoteBox.CaretIndex = NoteBox.Text.Length;
        }, DispatcherPriority.Loaded);
        StatusBar.Text = "已打开笔记";
    }

    private void OnNoteBoxChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNotePlaceholder();
        AdjustNoteBoxHeight();
        if (_syncingNoteBox || _activeAnnotation == null) return;
        _noteSaveTimer.Stop();
        _noteSaveTimer.Start();
        NoteHintText.Text = "保存中…";
    }

    private void UpdateNotePlaceholder()
    {
        NotePlaceholder.Visibility = string.IsNullOrEmpty(NoteBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>笔记框随内容增高，由外层 ScrollViewer 提供滚动条。</summary>
    private void AdjustNoteBoxHeight()
    {
        double width = NoteBox.ActualWidth;
        if (width <= 1)
            width = Math.Max(120, RightPanelCol.Width.Value > 40 ? RightPanelCol.Width.Value - 44 : 260);

        string measureText = string.IsNullOrEmpty(NoteBox.Text) ? " " : NoteBox.Text;
        if (!measureText.EndsWith('\n'))
            measureText += "\n";

        var dpi = VisualTreeHelper.GetDpi(this);
        var formatted = new FormattedText(
            measureText,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(NoteBox.FontFamily, NoteBox.FontStyle, NoteBox.FontWeight, NoteBox.FontStretch),
            NoteBox.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = width
        };

        NoteBox.Height = Math.Max(160, Math.Ceiling(formatted.Height) + 8);
    }

    private void SaveActiveNote()
    {
        if (_activeAnnotation == null || string.IsNullOrEmpty(_currentFilePath)) return;

        string note = NoteBox.Text ?? "";
        if (string.Equals(_activeAnnotation.Note, note, StringComparison.Ordinal))
        {
            NoteHintText.Text = "已保存";
            return;
        }

        _annotations.UpdateNote(_currentFilePath, _activeAnnotation.Id, note);
        _activeAnnotation.Note = note;
        ReaderControl.NotifyAnnotationNoteChanged(_activeAnnotation.Id, !string.IsNullOrWhiteSpace(note));
        NoteHintText.Text = "已保存";
    }

    private async void OnDeleteAnnotation(object sender, RoutedEventArgs e)
    {
        if (_activeAnnotation == null || string.IsNullOrEmpty(_currentFilePath)) return;

        var result = MessageBox.Show(
            this,
            "确定删除这条标记及其笔记吗？",
            "删除标记",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        string id = _activeAnnotation.Id;
        _annotations.Delete(_currentFilePath, id);
        await ReaderControl.RemoveAnnotationMarkAsync(id);
        CloseNotePanel();
        StatusBar.Text = "已删除标记";
    }

    private void OnCloseNotePanel(object sender, RoutedEventArgs e) => CloseNotePanel();

    private void CloseNotePanel()
    {
        _noteSaveTimer.Stop();
        SaveActiveNote();
        _activeAnnotation = null;
        ApplyRightCollapsed(true);
    }
}
