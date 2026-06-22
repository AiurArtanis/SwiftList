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
        listBox.PreviewMouseMove += List_PreviewMouseMove;
        // ponytail: register with handledEventsToo=true because the OLE system/ListBoxItem might handle it internally
        listBox.AddHandler(UIElement.QueryContinueDragEvent, new System.Windows.QueryContinueDragEventHandler(List_QueryContinueDrag), true);
    }

    private static void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(null);

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
                                    var dataObject = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[] { fullPath });

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
