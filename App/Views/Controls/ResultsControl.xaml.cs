using UserControl = System.Windows.Controls.UserControl;
using Border = System.Windows.Controls.Border;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;

namespace SwiftList.App
{
    public partial class ResultsControl : UserControl
    {
        public ResultsControl()
        {
            InitializeComponent();
        }

        public Border LoadingBorder => null!;
        public System.Windows.Controls.Control LoadingProgressBar => null!;
        public TextBlock LoadingTitleTextBlock => null!;
        public TextBlock LoadingStatsTextBlock => null!;
        public Button InstallServiceButton => null!;
        public ListBox ResultsListBox => LstResults;
        public Grid SearchResultsGrid => GridSearchResults;
        public Grid ActionsGrid => GridActions;
        public TextBlock ActionsTargetTextBlock => TxtActionsTarget;
        public ListBox ActionsListBox => LstActions;
    }
}
