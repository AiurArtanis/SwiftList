using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace SwiftList.App.Services
{
    /// <summary>
    /// Handles mouse input events for the actions list in shell menu mode.
    /// Extracted from ShellMenuPresenter to keep it under 300 lines.
    /// </summary>
    internal sealed class ShellMenuMouseInputHandler
    {
        private readonly ShellMenuPresenter _presenter;
        private readonly ISearchWindow _view;

        public ShellMenuMouseInputHandler(ShellMenuPresenter presenter, ISearchWindow view)
        {
            _presenter = presenter;
            _view = view;
        }

        public void HandleActionsPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null && item.Content is ActionMenuItem actionItem)
            {
                if (actionItem.IsSeparator || actionItem.IsSectionHeader || actionItem.IsDisabled)
                {
                    e.Handled = true;
                    return;
                }

                if (actionItem.HasSubMenu)
                {
                    e.Handled = true;
                    _view.LstActions.SelectedItem = actionItem;
                    _presenter.EnterSubMenu();
                }

                else
                {
                    e.Handled = true;
                    _view.LstActions.SelectedItem = actionItem;
                    _presenter.ExecuteSelectedAction();
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                if (child is FrameworkContentElement fce)
                    child = fce.Parent;
                else
                    child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }

            return null;
        }
    }
}
