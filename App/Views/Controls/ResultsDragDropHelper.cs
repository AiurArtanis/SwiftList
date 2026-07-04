using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftList.App.Views.Controls;

public static class ResultsDragDropHelper
{
    private static System.Windows.Point _dragStartPoint;
    private static bool _dragEndedInside;

    // When pressing on an item that's already part of a multi-selection, we suppress the list's
    // default "collapse to one" so a drag can carry all selected items. These remember the press
    // so a plain click (no drag) still collapses to the single item on button-up.
    private static object? _pendingItem;
    private static System.Windows.Controls.ListBox? _pendingList;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr WindowFromPoint(POINT Point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static void Register(System.Windows.Controls.ListBox listBox)
    {
        listBox.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
        listBox.PreviewMouseLeftButtonUp += List_PreviewMouseLeftButtonUp;
        listBox.PreviewMouseMove += List_PreviewMouseMove;
        // ponytail: register with handledEventsToo=true because the OLE system/ListBoxItem might handle it internally
        listBox.AddHandler(UIElement.QueryContinueDragEvent, new System.Windows.QueryContinueDragEventHandler(List_QueryContinueDrag), true);
    }

    private static void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _pendingItem = null;
        _pendingList = null;

        // No modifier + pressing a member of an existing multi-selection: keep the selection so a
        // drag carries all of it. Resolved as a single-select click on button-up if no drag runs.
        if (Keyboard.Modifiers == ModifierKeys.None && sender is System.Windows.Controls.ListBox lb)
        {
            var data = GetItemData(e.OriginalSource);
            if (data != null && lb.SelectedItems.Count > 1 && lb.SelectedItems.Contains(data))
            {
                e.Handled = true;
                _pendingItem = data;
                _pendingList = lb;
            }
        }
    }

    private static void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A suppressed press that never became a drag → treat as a plain click on the item.
        if (_pendingItem != null && _pendingList != null)
        {
            _pendingList.SelectedItem = _pendingItem;
            _pendingItem = null;
            _pendingList = null;
        }
    }

    private static object? GetItemData(object originalSource)
    {
        var dep = originalSource as DependencyObject;
        // Walk up via GetParent (not VisualTreeHelper.GetParent directly): the press can land on a
        // non-Visual ContentElement (e.g. a highlight Run inside the name TextBlock), which
        // VisualTreeHelper.GetParent rejects with InvalidOperationException. GetParent handles both.
        while (dep != null && dep is not ListBoxItem)
            dep = GetParent(dep);
        return (dep as ListBoxItem)?.DataContext;
    }

    // When the dragged item is part of a multi-selection, drag every selected file/folder.
    private static string[] CollectDragPaths(ItemsControl itemsControl, object dragged, string draggedPath)
    {
        var paths = new List<string>();
        if (itemsControl is System.Windows.Controls.ListBox lb && lb.SelectedItems.Count > 1 && lb.SelectedItems.Contains(dragged))
        {
            foreach (var obj in lb.SelectedItems)
            {
                try
                {
                    dynamic sr = obj;
                    string? p = sr.FullPath;
                    if (!string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)))
                        paths.Add(p);
                }
                catch { }
            }
        }
        if (paths.Count == 0) paths.Add(draggedPath);
        return paths.ToArray();
    }

    private static void List_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var mousePos = e.GetPosition(null);
        var diff = _dragStartPoint - mousePos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is ItemsControl itemsControl)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && dep != itemsControl)
                {
                    if (dep is ListBoxItem item)
                    {
                        var data = item.DataContext;
                        if (data != null)
                        {
                            try
                            {
                                dynamic searchResult = data;
                                string? fullPath = searchResult.FullPath;
                                if (!string.IsNullOrEmpty(fullPath) && (File.Exists(fullPath) || Directory.Exists(fullPath)))
                                {
                                    var paths = CollectDragPaths(itemsControl, data, fullPath);
                                    var dataObject = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, paths);

                                    // A drag is starting — don't collapse the selection on button-up.
                                    _pendingItem = null;
                                    _pendingList = null;
                                    _dragEndedInside = false;
                                    try
                                    {
                                        // Perform UI-thread synchronous drag
                                        DragDrop.DoDragDrop(item, dataObject, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Link);
                                    }
                                    finally
                                    {
                                        // ponytail: hide the search window immediately after the synchronous drag loop finishes, unless it ended inside the window
                                        if (!_dragEndedInside)
                                        {
                                            HideSearchWindows();
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        break;
                    }
                    dep = GetParent(dep);
                }
            }
        }
    }

    private static void List_QueryContinueDrag(object sender, System.Windows.QueryContinueDragEventArgs e)
    {
        var isLeftReleased = (e.KeyStates & DragDropKeyStates.LeftMouseButton) == 0;

        // ponytail: Detect the exact millisecond when the user releases the mouse (either drop or cancel) or presses Escape.
        // We must check isLeftReleased because WPF's internal OleDragSource implementation returns directly to OLE
        // without raising the routed event for System.Windows.DragAction.Drop/Cancel.
        var isDragEnding = e.Action == System.Windows.DragAction.Drop || 
                            e.Action == System.Windows.DragAction.Cancel || 
                            isLeftReleased || 
                            e.EscapePressed;

        if (isDragEnding)
        {
            _dragEndedInside = false;

            if (System.Windows.Application.Current != null)
            {
                if (GetCursorPos(out var mousePos))
                {
                    var hwndUnderCursor = WindowFromPoint(mousePos);
                    uint pid = 0;
                    if (hwndUnderCursor != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hwndUnderCursor, out pid);
                    }

                    var isInsideApp = (hwndUnderCursor != IntPtr.Zero && pid == (uint)Environment.ProcessId);

                    if (isInsideApp)
                    {
                        _dragEndedInside = true;
                    }
                    else
                    {
                        // ponytail: hide the search window immediately via WPF HideWindow before OLE drop target blocks the thread
                        HideSearchWindows();
                        _dragEndedInside = true; // prevent finally block from doing it again
                    }
                }
            }
        }
    }

    private static void HideSearchWindows()
    {
        if (System.Windows.Application.Current != null)
        {
            var windows = new List<Window>();
            foreach (Window w in System.Windows.Application.Current.Windows)
            {
                windows.Add(w);
            }

            foreach (var w in windows)
            {
                if (w is SwiftList.App.QuickSearchWindow qsw)
                {
                    qsw.HideWindow();
                }
                else if (w is SwiftList.App.InlineSearchWindow isw)
                {
                    Services.InlineSearchManager.Instance.CloseInlineSearch("DragDropCompleted");
                }
            }
        }
    }

    private static DependencyObject? GetParent(DependencyObject dep)
    {
        if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(dep);
        }
        else if (dep is FrameworkContentElement fce)
        {
            return fce.Parent;
        }
        return null;
    }
}
