using Azw3Reader.App.Controls;
using Azw3Reader.App.ViewModels;
using Azw3Reader.Core.Models;
using Azw3Reader.Core.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace Azw3Reader.App;

public partial class MainWindow : Window
{
    private readonly ReaderViewModel _vm = new();
    private ExtractionResult? _currentBook;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // 注册拖放
        AllowDrop = true;
        Drop += OnDropFile;
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AZW3/MOBI 文件|*.azw3;*.mobi;*.azw;*.kf8|所有文件|*.*",
            Title = "选择一本书籍"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadBook(dialog.FileName);
        }
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
            StatusBar.Text = "正在解析...";
            var extractor = new Azw3Extractor();
            _currentBook = extractor.Extract(filePath);

            // 更新 UI
            BookTitle.Text = _currentBook.BookInfo.Title;
            BookAuthor.Text = _currentBook.BookInfo.Author;
            FileFormatText.Text = _currentBook.BookInfo.FileFormat;

            // 加载目录
            ChapterList.ItemsSource = _currentBook.Chapters;

            // 加载到阅读控件
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
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is Chapter chapter)
        {
            ReaderControl.GoToChapter(chapter.Index);
        }
    }
}
