using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.PluginSdk;
using SwiftList.App.Helpers;

namespace SwiftList.App.ViewModels.Settings.Plugins;

public class PluginConfigFieldViewModel : ViewModelBase
{
    private readonly Action? _onValueChanged;
    private object? _localValueStore;

    public string PluginId { get; }
    public PluginConfigField SchemaField { get; }
    public UserSettings Settings { get; }

    public string Label => string.IsNullOrEmpty(SchemaField.LabelKey) ? string.Empty : TranslationService.Get(SchemaField.LabelKey);
    public string Description => string.IsNullOrEmpty(SchemaField.DescriptionKey) ? string.Empty : TranslationService.Get(SchemaField.DescriptionKey);
    public ConfigFieldType FieldType => SchemaField.FieldType;
    public List<string>? Choices => SchemaField.Choices?.Select(c => TranslationService.Get(c)).ToList();

    public bool IsBoolean => FieldType == ConfigFieldType.Boolean;
    public bool IsText => FieldType == ConfigFieldType.Text;
    public bool IsInteger => FieldType == ConfigFieldType.Integer;
    public bool IsChoice => FieldType == ConfigFieldType.Choice;
    public bool IsArray => FieldType == ConfigFieldType.Array;
    public bool IsObject => FieldType == ConfigFieldType.Object;
    public bool IsIconField => SchemaField.Key.Equals("Icon", StringComparison.OrdinalIgnoreCase);
    public bool IsSimpleField => IsBoolean || IsText || IsInteger || IsChoice;

    public ObservableCollection<PluginConfigFieldViewModel> Children { get; } = new();
    public ObservableCollection<PluginConfigArrayItemViewModel> ArrayItems { get; } = new();

    public ICommand AddCommand { get; }

    public object? LocalValueStore
    {
        get => _localValueStore ?? SchemaField.DefaultValue;
        set
        {
            _localValueStore = ConfigValueHelper.UnpackValue(value);
            OnPropertyChanged(nameof(Value));
            _onValueChanged?.Invoke();
        }
    }

    public object? Value
    {
        get
        {
            if (_onValueChanged != null) return LocalValueStore;
            if (IsObject || IsArray) return this;
            return ConfigValueHelper.UnpackValue(Settings.GetPluginSetting(PluginId, SchemaField.Key, SchemaField.DefaultValue));
        }
        set
        {
            var converted = ConfigValueHelper.ConvertValue(value, FieldType);
            LocalValueStore = converted;
            if (_onValueChanged == null)
            {
                OnPropertyChanged();
            }
        }
    }

    public PluginConfigFieldViewModel(string pluginId, PluginConfigField field, UserSettings settings, Action? onValueChanged = null)
    {
        PluginId = pluginId;
        SchemaField = field;
        Settings = settings;
        _onValueChanged = onValueChanged;
        AddCommand = new RelayCommand(AddArrayItem);

        if (_onValueChanged == null)
        {
            LoadChildrenAndArrayItems();
        }
    }

    public void Commit()
    {
        if (IsObject)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in Children)
            {
                child.Commit();
                dict[child.SchemaField.Key] = child.LocalValueStore;
            }
            _localValueStore = dict;
        }
        else if (IsArray)
        {
            var list = new List<object?>();
            foreach (var item in ArrayItems)
            {
                list.Add(item.GetValue());
            }
            _localValueStore = list;
        }

        if (_onValueChanged == null)
        {
            Settings.SetPluginSetting(PluginId, SchemaField.Key, LocalValueStore);
        }
    }

    public void Reload()
    {
        _localValueStore = null;
        Children.Clear();
        ArrayItems.Clear();
        LoadChildrenAndArrayItems();
        OnPropertyChanged(nameof(Value));
    }

    private void LoadChildrenAndArrayItems()
    {
        if (IsObject && SchemaField.SubFields != null)
        {
            var rawSetting = Settings.GetPluginSetting<object?>(PluginId, SchemaField.Key, null);
            var dict = ConfigValueHelper.UnpackValue(rawSetting) as Dictionary<string, object>
                       ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var sf in SchemaField.SubFields)
            {
                dict.TryGetValue(sf.Key, out var val);
                var childVM = new PluginConfigFieldViewModel(PluginId, sf, Settings, SaveObjectFromChildren)
                {
                    LocalValueStore = ConfigValueHelper.UnpackValue(val ?? sf.DefaultValue)
                };
                Children.Add(childVM);
            }
        }
        else if (IsArray)
        {
            var rawSetting = Settings.GetPluginSetting<object?>(PluginId, SchemaField.Key, null);
            var list = ConfigValueHelper.UnpackValue(rawSetting) as System.Collections.IEnumerable
                       ?? (SchemaField.DefaultValue as System.Collections.IEnumerable);

            if (list != null)
            {
                var hasAnyVal = false;
                foreach (var item in list)
                {
                    var unpackedItem = ConfigValueHelper.UnpackValue(item);
                    if (unpackedItem is Dictionary<string, object> d)
                    {
                        if (d.Values.Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())))
                        {
                            hasAnyVal = true;
                            break;
                        }
                    }
                    else if (unpackedItem != null && !string.IsNullOrWhiteSpace(unpackedItem.ToString()))
                    {
                        hasAnyVal = true;
                        break;
                    }
                }

                if (!hasAnyVal && rawSetting != null)
                {
                    list = SchemaField.DefaultValue as System.Collections.IEnumerable;
                }

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        AddArrayItemViewModel(ConfigValueHelper.UnpackValue(item));
                    }
                }
            }
        }
    }

    private void AddArrayItem()
    {
        object? newItem = null;
        if (SchemaField.SubFields != null && SchemaField.SubFields.Count > 0)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var sf in SchemaField.SubFields)
            {
                dict[sf.Key] = sf.DefaultValue;
            }
            newItem = dict;
        }
        else
        {
            newItem = string.Empty;
        }

        AddArrayItemViewModel(newItem);
        SaveArrayFromChildren();
    }

    private void AddArrayItemViewModel(object? itemValue)
    {
        PluginConfigArrayItemViewModel? itemVM = null;
        itemVM = new PluginConfigArrayItemViewModel(this, itemValue, () =>
        {
            ArrayItems.Remove(itemVM!);
            SaveArrayFromChildren();
        });
        ArrayItems.Add(itemVM);
    }

    public void OnChildChanged()
    {
        if (IsArray) SaveArrayFromChildren();
        else if (IsObject) SaveObjectFromChildren();
        else _onValueChanged?.Invoke();
    }

    private void SaveObjectFromChildren()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in Children)
        {
            dict[child.SchemaField.Key] = child.LocalValueStore;
        }
        _localValueStore = dict;
        OnPropertyChanged(nameof(Value));
        _onValueChanged?.Invoke();
    }

    private void SaveArrayFromChildren()
    {
        _localValueStore = ArrayItems.Select(item => item.GetValue()).ToList();
        OnPropertyChanged(nameof(Value));
        _onValueChanged?.Invoke();
    }
}
