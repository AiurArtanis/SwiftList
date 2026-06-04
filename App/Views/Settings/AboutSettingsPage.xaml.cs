namespace SwiftList.App.Views.Settings
{
    public partial class AboutSettingsPage : System.Windows.Controls.UserControl
    {
        public string AppVersion
        {
            get
            {
                var version = typeof(AboutSettingsPage).Assembly.GetName().Version;
                return $"Version {version?.ToString(3)}";
            }
        }

        public AboutSettingsPage()
        {
            InitializeComponent();
            DataContext = this;
        }
    }
}
