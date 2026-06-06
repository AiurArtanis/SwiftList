using System.Windows;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.App
{
    public class AppSearchResult : System.ComponentModel.INotifyPropertyChanged, SwiftList.PluginSdk.ISearchResult
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string ParentDir { get; set; } = string.Empty;
        public string ContextDirectory { get; set; } = string.Empty;
        public bool IsDir { get; set; }
        public string Drive { get; set; } = string.Empty;
        public string ResultKind { get; set; } = "File";
        public int Index { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public bool IsApplication => ResultKind == "Application";
        public bool IsPluginSearchAction => ResultKind == "PluginAction";
        public bool IsSearchSectionHeader => ResultKind == "SectionHeader";
        public bool IsJumpToExplorerPath => ResultKind == "JumpToExplorerPath";
        public bool IsEmptyResult => ResultKind == "Empty";
        public bool IsInstantResult => ResultKind == "InstantResult";
        public bool IsListItem => ResultKind == "ListItem";
        public string ResultTypeText => IsInstantResult ? TranslationManager.Instance["Model_TypePlugin"] : (IsApplication ? TranslationManager.Instance["Model_TypeApp"] : (IsDir ? TranslationManager.Instance["Model_TypeFolder"] : TranslationManager.Instance["Model_TypeFile"]));
        public string DisplayPath => IsApplication ? ParentDir : FullPath;
        public uint PluginActionId { get; set; }
        public string PluginActionArgumentText { get; set; } = string.Empty;
        public System.Windows.Media.ImageSource? IconOverride { get; set; }
        public string InstantResultActionType { get; set; } = "Copy";
        public string InstantResultActionArgument { get; set; } = string.Empty;
        public string? TabCompletion { get; set; }

        // Visual properties

        public string IconData => FullPath == "__SHOW_MORE__"
            ? "M14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"
            : (IsDir
                // Folder icon (filled folder shape)
                ? "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"
                // File icon (document shape)
                : "M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm4 18H6V4h7v5h5v11z");

        private System.Windows.Media.ImageSource? _icon;
        private bool _iconLoadingStarted;

        public System.Windows.Media.ImageSource? Icon
        {
            get
            {
                if (IsEmptyResult || IsListItem)
                    return null;
                if (IconOverride != null)
                    return IconOverride;

                if (_icon == null)
                {
                    _icon = ShellIconHelper.GetIconFromCacheOnly(FullPath, IsDir, out bool needsLoad);
                    if (needsLoad && !_iconLoadingStarted)
                    {
                        _iconLoadingStarted = true;
                        LoadIconAsync();
                    }
                }
                return _icon;
            }
        }

        private void LoadIconAsync()
        {
            string pathCopy = FullPath;
            bool isDirCopy = IsDir;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var realIcon = ShellIconHelper.GetIconForPath(pathCopy, isDirCopy);
                    if (realIcon != null)
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null)
                        {
                            app.Dispatcher.BeginInvoke(new System.Action(() =>
                            {
                                _icon = realIcon;
                                OnPropertyChanged(nameof(Icon));
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else
                        {
                            _icon = realIcon;
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            });
        }

        private string _shortcutHint = string.Empty;
        public string ShortcutHint
        {
            get => _shortcutHint;
            set
            {
                if (_shortcutHint != value)
                {
                    _shortcutHint = value;
                    OnPropertyChanged(nameof(ShortcutHint));
                }
            }
        }

        private Visibility _shortcutVisibility = Visibility.Collapsed;
        public Visibility ShortcutVisibility
        {
            get => _shortcutVisibility;
            set
            {
                if (_shortcutVisibility != value)
                {
                    _shortcutVisibility = value;
                    OnPropertyChanged(nameof(ShortcutVisibility));
                }
            }
        }

        // Lazy-loaded File Date Modified
        private System.DateTime? _dateModified;
        public System.DateTime DateModified
        {
            get
            {
                if (_dateModified.HasValue) return _dateModified.Value;
                try
                {
                    string physicalPath = FullPath;
                    if (System.IO.File.Exists(physicalPath) || System.IO.Directory.Exists(physicalPath))
                    {
                        _dateModified = System.IO.Directory.Exists(physicalPath)
                            ? System.IO.Directory.GetLastWriteTime(physicalPath)
                            : System.IO.File.GetLastWriteTime(physicalPath);
                    }
                    else
                    {
                        _dateModified = System.DateTime.MinValue;
                    }
                }
                catch
                {
                    _dateModified = System.DateTime.MinValue;
                }
                return _dateModified.Value;
            }
        }

        public string DateModifiedText
        {
            get
            {
                var dt = DateModified;
                return dt == System.DateTime.MinValue ? TranslationManager.Instance["Model_TimeUnknown"] : dt.ToString("yyyy/MM/dd HH:mm");
            }
        }

        private readonly System.Collections.Generic.Dictionary<string, string> _extendedValues = new(System.StringComparer.OrdinalIgnoreCase);

        public string this[string columnId]
        {
            get
            {
                if (string.IsNullOrEmpty(columnId)) return string.Empty;

                if (_extendedValues.TryGetValue(columnId, out var cachedVal))
                    return cachedVal;

                foreach (var provider in SwiftList.App.Services.PluginManager.Instance.ResultColumnProviders)
                {
                    if (System.Linq.Enumerable.Any(provider.GetColumns(), c => c.ColumnId.Equals(columnId, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            string cellVal = provider.GetCellValue(this, columnId);
                            _extendedValues[columnId] = cellVal;
                            return cellVal;
                        }
                        catch
                        {
                            return string.Empty;
                        }
                    }
                }

                return string.Empty;
            }
            set
            {
                _extendedValues[columnId] = value;
                OnPropertyChanged("Item[]");
            }
        }
    }
}
