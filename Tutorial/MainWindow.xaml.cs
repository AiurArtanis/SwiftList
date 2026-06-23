using System.Windows;
using System.Windows.Input;
using SwiftList.Tutorial.ViewModels;

namespace SwiftList.Tutorial;

public partial class MainWindow : Window
{
    private readonly TutorialViewModel _viewModel;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new TutorialViewModel();
        this.DataContext = _viewModel;
        
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ShellWindow: " + GetShellWindow());
            
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                var shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic dShell = shell;
                    dynamic windows = dShell.Windows();
                    if (windows != null)
                    {
                        int count = windows.Count;
                        sb.AppendLine("Windows Count: " + count);
                        for (var i = 0; i < count; i++)
                        {
                            dynamic window = windows.Item(i);
                            if (window != null)
                            {
                                dynamic w = window;
                                var wHwnd = new IntPtr(w.HWND);
                                var sbClass = new System.Text.StringBuilder(256);
                                GetClassName(wHwnd, sbClass, sbClass.Capacity);
                                sb.AppendLine($"Index: {i}, Name: {w.Name}, HWND: {wHwnd}, Class: {sbClass}");
                                
                                try
                                {
                                    dynamic doc = w.Document;
                                    if (doc != null)
                                    {
                                        dynamic selected = doc.SelectedItems();
                                        sb.AppendLine($"  Selected count: {selected.Count}");
                                    }
                                }
                                catch (Exception ex) { sb.AppendLine("  Doc Error: " + ex.Message); }
                            }
                        }
                    }
                }
            }
            System.IO.File.WriteAllText(@"d:\Dev\cs\SwiftList\scratch\output.txt", sb.ToString());
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(@"d:\Dev\cs\SwiftList\scratch\output.txt", "Fatal Error: " + ex.ToString());
        }

        // Allow window dragging
        this.MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => this.Close();

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentStep == 0)
        {
            _viewModel.CurrentStep = 1;
        }
        else if (_viewModel.CurrentStep >= 1 && _viewModel.CurrentStep <= 5)
        {
            _viewModel.CurrentStep++;
        }
        else if (_viewModel.CurrentStep == 6)
        {
            this.Close();
        }
    }
}
