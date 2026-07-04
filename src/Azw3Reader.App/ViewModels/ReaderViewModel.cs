using Azw3Reader.Core.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Azw3Reader.App.ViewModels;

public class ReaderViewModel : INotifyPropertyChanged
{
    private ExtractionResult? _book;
    private int _currentChapter;

    public ExtractionResult? Book
    {
        get => _book;
        set { _book = value; OnPropertyChanged(); OnPropertyChanged(nameof(BookTitle)); }
    }

    public string BookTitle => _book?.BookInfo.Title ?? "未打开书籍";
    public string BookAuthor => _book?.BookInfo.Author ?? "";
    public string CoverImage => _book?.BookInfo.CoverImageUrl ?? "";

    public int CurrentChapter
    {
        get => _currentChapter;
        set { _currentChapter = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Chapter> Chapters { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string name = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
