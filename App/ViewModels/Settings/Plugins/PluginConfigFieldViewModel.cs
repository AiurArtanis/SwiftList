using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Services;
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
    public string GroupKey => SchemaField.GroupKey;
    public string GroupName => string.IsNullOrEmpty(GroupKey) ? string.Empty : (TranslationService.Get(GroupKey) ?? string.Empty);
    public ConfigFieldType FieldType => SchemaField.FieldType;
    public List<string>? Choices => SchemaField.Choices?.Select(c => TranslationService.Get(c)).ToList();

    public bool IsBoolean => FieldType == ConfigFieldType.Boolean;
    public bool IsText => FieldType == ConfigFieldType.Text;
    public bool IsInteger => FieldType == ConfigFieldType.Integer;
    public bool IsChoice => FieldType == ConfigFieldType.Choice;
    public bool IsArray => FieldType == ConfigFieldType.Array;
    public bool IsObject => FieldType == ConfigFieldType.Object;
    public bool IsGroup => FieldType == ConfigFieldType.Group;
    public bool IsStringList => FieldType == ConfigFieldType.StringList;
    public bool IsHotkey => FieldType == ConfigFieldType.Hotkey;
    public bool IsIconField => SchemaField.Key.Equals("Icon", StringComparison.OrdinalIgnoreCase);
    public bool IsSimpleField => IsBoolean || IsText || IsInteger || IsChoice || IsStringList || IsHotkey;

    public ObservableCollection<PluginConfigFieldViewModel> Children { get; } = new();
    public ObservableCollection<PluginConfigArrayItemViewModel> ArrayItems { get; } = new();

    public ICommand AddCommand { get; }

    public object? LocalValueStore
    {
        get
        {
            if (_localValueStore == null)
            {
                if (IsGroup)
                {
                    _localValueStore = null;
                }
                else if (_onValueChanged != null)
                {
                    _localValueStore = SchemaField.DefaultValue;
                }
                else
                {
                    _localValueStore = ConfigValueHelper.UnpackValue(Settings.GetPluginSetting(PluginId, SchemaField.Key, SchemaField.DefaultValue));
                }
            }
            return _localValueStore;
        }
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
            if (IsObject || IsArray || IsGroup) return this;
            if (IsStringList)
            {
                if (LocalValueStore is System.Collections.IEnumerable en && !(LocalValueStore is string))
                {
                    var items = new List<string>();
                    foreach (var item in en) items.Add(item?.ToString() ?? string.Empty);
                    return string.Join("\r\n", items);
                }
                return LocalValueStore?.ToString() ?? string.Empty;
            }
            return LocalValueStore;
        }
        set
        {
            if (IsStringList && value is string strVal)
                LocalValueStore = strVal.Split('\n').Select(s => s.TrimEnd('\r').Trim()).ToList();
            else
                LocalValueStore = ConfigValueHelper.ConvertValue(value, FieldType);
            if (_onValueChanged == null) OnPropertyChanged();
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
        if (IsGroup)
        {
            foreach (var child in Children)
            {
                child.Commit();
            }
            return;
        }
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
            if (IsStringList && LocalValueStore is System.Collections.IEnumerable en && !(LocalValueStore is string))
            {
                var cleaned = new List<string>();
                foreach (var item in en) { var s = item?.ToString()?.Trim(); if (!string.IsNullOrEmpty(s)) cleaned.Add(s); }
                Settings.SetPluginSetting(PluginId, SchemaField.Key, cleaned);
            }
            else Settings.SetPluginSetting(PluginId, SchemaField.Key, LocalValueStore);
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
        if (IsGroup && SchemaField.SubFields != null)
        {
            foreach (var sf in SchemaField.SubFields)
            {
                var childVM = new PluginConfigFieldViewModel(PluginId, sf, Settings, null);
                Children.Add(childVM);
            }
        }
        else if (IsObject && SchemaField.SubFields != null)
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
