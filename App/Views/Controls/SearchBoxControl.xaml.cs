using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;

namespace SwiftList.App
{
    public partial class SearchBoxControl : UserControl
    {
        public SearchBoxControl()
        {
            InitializeComponent();
        }

        public TextBox SearchTextBox => TxtSearch;
        public TextBlock PlaceholderTextBlock => TxtPlaceholder;
    }
}
