using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwiftList.Tutorial.Models;

public class TodoItem : INotifyPropertyChanged
{
    private string _text;
    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set { _isCompleted = value; OnPropertyChanged(); }
    }

    public TodoItem(string text, bool isCompleted)
    {
        _text = text;
        _isCompleted = isCompleted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
