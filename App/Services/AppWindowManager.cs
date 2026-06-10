using System.Windows;

namespace SwiftList.App.Services;

public static class AppWindowManager
{
    private static SettingsWindow? _settingsWindow;
    private static SearchWindow? _searchWindow;

    public static void ShowSettingsWindow(string? targetSection = null) => System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                                                                {
                                                                                    if (_settingsWindow == null)
                                                                                    {
                                                                                        _settingsWindow = new SettingsWindow();
                                                                                        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                                                                                    }

                                                                                    if (!_settingsWindow.IsVisible)
                                                                                        _settingsWindow.Show();

                                                                                    if (_settingsWindow.WindowState == WindowState.Minimized)
                                                                                        _settingsWindow.WindowState = WindowState.Normal;

                                                                                    if (!string.IsNullOrEmpty(targetSection))
                                                                                    {
                                                                                        _settingsWindow.SelectSection(targetSection);
                                                                                    }

                                                                                    _settingsWindow.Activate();
                                                                                });

    public static void ShowSearchWindow() => System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                                  {
                                                      if (_searchWindow == null)
                                                      {
                                                          _searchWindow = new SearchWindow();
                                                          _searchWindow.Closed += (_, _) => _searchWindow = null;
                                                      }

                                                      if (!_searchWindow.IsVisible)
                                                          _searchWindow.Show();

                                                      if (_searchWindow.WindowState == WindowState.Minimized)
                                                          _searchWindow.WindowState = WindowState.Normal;

                                                      _searchWindow.Activate();
                                                  });

    public static void CloseAllManagedWindows() => System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                                        {
                                                            _settingsWindow?.Close();
                                                            _settingsWindow = null;
                                                            _searchWindow?.Close();
                                                            _searchWindow = null;
                                                        });
}
