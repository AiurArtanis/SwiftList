using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftList.App.Helpers;

/// <summary>
/// An ObservableCollection subclass that supports bulk range updates (ReplaceRange/AddRange)
/// while triggering only a single CollectionChanged notification to eliminate WPF rendering churn.
/// </summary>
/// <typeparam name="T">Type of items in collection</typeparam>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private bool _isNotificationSuspended;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_isNotificationSuspended)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_isNotificationSuspended)
        {
            base.OnPropertyChanged(e);
        }
    }

    /// <summary>
    /// Clears the collection and adds a new range of items, raising only a single Reset notification.
    /// </summary>
    public void ReplaceRange(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));

        _isNotificationSuspended = true;
        try
        {
            Items.Clear();
            foreach (var item in collection)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _isNotificationSuspended = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
