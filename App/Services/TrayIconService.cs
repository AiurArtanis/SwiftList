using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using SwiftList.App.ViewModels;
using SwiftList.App.ViewModels.Search;
using SwiftList.Core;
using Application = System.Windows.Application;

namespace SwiftList.App.Services
{
    public class TrayIconService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private readonly QuickSearchViewModel _viewModel;
        private readonly Action _showWindowAction;
        private readonly Action _toggleVisibilityAction;

        private System.Windows.Controls.ContextMenu? _wpfContextMenu;
        private System.Windows.Controls.MenuItem? _wpfItemShowWindow;
        private System.Windows.Controls.MenuItem? _wpfItemToggleHotkeys;
        private System.Windows.Controls.MenuItem? _wpfItemSettings;
        private System.Windows.Controls.MenuItem? _wpfItemAbout;
        private System.Windows.Controls.Separator? _wpfCleanExitSeparator;
        private System.Windows.Controls.MenuItem? _wpfItemCleanExit;
        private System.Windows.Controls.MenuItem? _wpfItemExit;
        private Window? _dummyWindow;
        private bool _isHotkeysDisabled;

        public TrayIconService(QuickSearchViewModel viewModel, Action showWindowAction, Action toggleVisibilityAction)
        {
            _viewModel = viewModel;
            _showWindowAction = showWindowAction;
            _toggleVisibilityAction = toggleVisibilityAction;
            InitializeNotifyIcon();

            TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
            UpdateMenuTexts();
        }

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateMenuTexts();
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "SwiftList"
            };

            try
            {
                var resourceUri = new Uri("pack://application:,,,/SwiftList.App;component/logo.ico", UriKind.Absolute);
                var resourceInfo = Application.GetResourceStream(resourceUri);

                if (resourceInfo != null)
                {
                    using var stream = resourceInfo.Stream;
                    _notifyIcon.Icon = new Icon(stream);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[TrayIconService] Failed to load logo resource: {ex.Message}", SwiftList.Core.LogLevel.Error);
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _toggleVisibilityAction();
                }
                else if (e.Button == MouseButtons.Right)
                {
                    ShowWpfContextMenu();
                }
            };
        }

        private void InitializeWpfContextMenu()
        {
            _wpfContextMenu = new System.Windows.Controls.ContextMenu();

            _wpfItemShowWindow = new System.Windows.Controls.MenuItem
            {
                Icon = CreateIcon("\uE721", "AccentBlue")
            };
            _wpfItemShowWindow.Click += (s, e) => ShowSearchWindow();

            _wpfItemToggleHotkeys = new System.Windows.Controls.MenuItem();
            _wpfItemToggleHotkeys.Click += (s, e) => ToggleHotkeys();

            _wpfItemSettings = new System.Windows.Controls.MenuItem
            {
                Icon = CreateIcon("\uE713", "MenuText")
            };
            _wpfItemSettings.Click += (s, e) => ShowSettingsWindow();

            _wpfItemAbout = new System.Windows.Controls.MenuItem
            {
                Icon = CreateIcon("\uE946", "AccentBlue")
            };
            _wpfItemAbout.Click += (s, e) => ShowSettingsWindow("About");

            _wpfItemCleanExit = new System.Windows.Controls.MenuItem
            {
                Icon = CreateIcon("\uE74D", "AccentBlue")
            };
            _wpfItemCleanExit.Click += (s, e) => TrayCleanExitHelper.CleanExit();

            _wpfItemExit = new System.Windows.Controls.MenuItem
            {
                Icon = CreateIcon("\uF3B1", "MenuText")
            };
            _wpfItemExit.Click += (s, e) => Application.Current.Shutdown();

            _wpfCleanExitSeparator = new System.Windows.Controls.Separator();

            _wpfContextMenu.Items.Add(_wpfItemShowWindow);
            _wpfContextMenu.Items.Add(_wpfItemToggleHotkeys);
            _wpfContextMenu.Items.Add(_wpfItemSettings);
            _wpfContextMenu.Items.Add(new System.Windows.Controls.Separator());
            _wpfContextMenu.Items.Add(_wpfItemAbout);
            _wpfContextMenu.Items.Add(new System.Windows.Controls.Separator());
            _wpfContextMenu.Items.Add(_wpfCleanExitSeparator);
            _wpfContextMenu.Items.Add(_wpfItemCleanExit);
            _wpfContextMenu.Items.Add(_wpfItemExit);

            UpdateMenuTexts();
        }

        private static UIElement CreateIcon(string glyph, string resourceKey)
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                Text = glyph,
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            tb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, resourceKey);
            return tb;
        }

        private void ShowWpfContextMenu()
        {
            if (_wpfContextMenu == null)
            {
                InitializeWpfContextMenu();
            }

            UpdateCleanExitVisibility();

            if (_dummyWindow != null)
            {
                try { _dummyWindow.Close(); } catch { }
                _dummyWindow = null;
            }

            _dummyWindow = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true
            };

            _dummyWindow.Show();
            _dummyWindow.Activate();

            _wpfContextMenu!.PlacementTarget = _dummyWindow;
            _wpfContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;

            RoutedEventHandler? closedHandler = null;
            closedHandler = (s, e) =>
            {
                _wpfContextMenu.Closed -= closedHandler;
                try
                {
                    _dummyWindow?.Close();
                }
                catch { }
                _dummyWindow = null;
            };
            _wpfContextMenu.Closed += closedHandler;

            _wpfContextMenu.IsOpen = true;
        }

        private void UpdateMenuTexts()
        {
            if (_wpfItemShowWindow != null) _wpfItemShowWindow.Header = TranslationManager.Instance["Tray_ShowWindow"];
            if (_wpfItemSettings != null) _wpfItemSettings.Header = TranslationManager.Instance["Tray_Settings"];
            if (_wpfItemAbout != null) _wpfItemAbout.Header = TranslationManager.Instance["Tray_About"];
            if (_wpfItemCleanExit != null) _wpfItemCleanExit.Header = TranslationManager.Instance["Tray_CleanExit"];
            if (_wpfItemExit != null) _wpfItemExit.Header = TranslationManager.Instance["Tray_Exit"];
            UpdateHotkeysMenuState();
        }

        private void ToggleHotkeys()
        {
            if (_wpfItemToggleHotkeys == null) return;
            _isHotkeysDisabled = !_isHotkeysDisabled;
            if (App.HookClient != null)
            {
                App.HookClient.IsHotkeysDisabled = _isHotkeysDisabled;
            }
            UpdateHotkeysMenuState();
        }

        private void UpdateHotkeysMenuState()
        {
            if (_wpfItemToggleHotkeys == null) return;
            _wpfItemToggleHotkeys.Header = TranslationManager.Instance["Tray_ToggleHotkeys"];
            bool isDisabled = App.HookClient != null ? App.HookClient.IsHotkeysDisabled : _isHotkeysDisabled;
            if (isDisabled)
            {
                _wpfItemToggleHotkeys.Icon = CreateIcon("\uE73E", "AccentBlue");
            }
            else
            {
                _wpfItemToggleHotkeys.Icon = CreateIcon("\uE71A", "MenuText");
            }
        }

        private void ShowSettingsWindow(string? targetSection = null)
        {
            App.ShowSettingsWindow(targetSection);
        }

        private void ShowSearchWindow()
        {
            App.ShowSearchWindow();
        }

        private void UpdateCleanExitVisibility()
        {
            if (_wpfItemCleanExit == null) return;
            var visibility = TrayCleanExitHelper.IsOnlyAppProcessRunning() ? Visibility.Visible : Visibility.Collapsed;
            _wpfItemCleanExit.Visibility = visibility;
            if (_wpfCleanExitSeparator != null)
            {
                _wpfCleanExitSeparator.Visibility = visibility;
            }
        }

        public void Dispose()
        {
            TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
            App.CloseAllManagedWindows();

            if (_dummyWindow != null)
            {
                try { _dummyWindow.Close(); } catch { }
                _dummyWindow = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
