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
    /// <summary>
    /// Whether the update currently being notified grew an existing set of content rather than
    /// replacing it with a different one.
    /// </summary>
    /// <remarks>
    /// A Reset notification says "everything changed" and nothing more, which is all a view needs in
    /// order to redraw but not enough for it to decide whether the user's place in the list is still
    /// meaningful. Progressive rendering makes that distinction matter: a search paints several times
    /// as results arrive, and each of those repaints is the same list getting longer, not a new one.
    /// Set before the notifications go out so a handler can read it either inline or from a callback it
    /// schedules; it stays valid until the next update.
    /// </remarks>
    public bool LastUpdateExtendedContent { get; private set; }

    public void ReplaceRange(IEnumerable<T> collection)
    {
        LastUpdateExtendedContent = false;
        ReplaceRangeCore(collection);
    }

    private void ReplaceRangeCore(IEnumerable<T> collection)
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
    /// <summary>
    /// Above this many rows changing, one Reset costs less than the per-row notifications. Each Replace
    /// or Add below is a separate CollectionChanged the ItemsControl, its CollectionView and its
    /// container generator all have to process -- virtualization saves the rendering of an off-screen
    /// row, not the bookkeeping for it. A result set in the hundreds of thousands therefore raises
    /// hundreds of thousands of notifications on the UI thread and the window stops responding, looking
    /// for all the world as though the results never arrived. A viewport is a few dozen rows, so a few
    /// hundred granular updates still beats a Reset; past that it stops being true.
    /// </summary>
    private const int ResetInsteadOfReconcileThreshold = 512;

    /// <param name="extendsContent">
    /// True when <paramref name="target"/> is a longer take on the content already shown -- the next
    /// paint of a search that is still streaming -- rather than a different result set. Surfaced to
    /// views through <see cref="LastUpdateExtendedContent"/>; it changes nothing about the update itself.
    /// </param>
    public void ReconcileTo(IReadOnlyList<T> target, Func<T, T, bool> equals, bool extendsContent = false)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (equals == null) throw new ArgumentNullException(nameof(equals));

        LastUpdateExtendedContent = extendsContent;

        if (Math.Abs(target.Count - Items.Count) >= ResetInsteadOfReconcileThreshold)
        {
            ReplaceRangeCore(target);
            return;
        }

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
