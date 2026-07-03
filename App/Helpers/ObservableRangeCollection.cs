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

    /// <summary>
    /// Updates the collection to match <paramref name="target"/> using granular Replace/Add/Remove
    /// notifications instead of a bulk Reset. A Reset (Clear + re-add) makes WPF discard and
    /// regenerate every container from the top — the "expand from the top" flicker. Here only the
    /// rows that differ are replaced in place, so a recycling ListBox reuses its containers for a
    /// cheap per-row refresh; the differing tail is then appended or trimmed.
    /// </summary>
    public void ReconcileTo(IReadOnlyList<T> target, Func<T, T, bool> equals)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (equals == null) throw new ArgumentNullException(nameof(equals));

        var shared = Math.Min(Items.Count, target.Count);
        for (var i = 0; i < shared; i++)
        {
            if (!equals(Items[i], target[i]))
                this[i] = target[i]; // Replace notification for this row only
        }

        for (var i = Items.Count; i < target.Count; i++)
            Add(target[i]);

        for (var i = Items.Count - 1; i >= target.Count; i--)
            RemoveAt(i);
    }
}
