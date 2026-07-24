using System.Windows;
using SwiftList.App.Views.QuickSearchWindow.Helpers;

namespace SwiftList.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchWindowControllerTests
{
    [TestMethod]
    public void DetermineToggleAction_WindowNotVisible_ReturnsShow()
    {
        var action = QuickSearchWindowController.DetermineToggleAction(isVisible: false, WindowState.Normal, reopenAsFullWindowSetting: true);

        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Show, action);
    }

    [TestMethod]
    public void DetermineToggleAction_WindowMinimized_ReturnsShow()
    {
        var action = QuickSearchWindowController.DetermineToggleAction(isVisible: true, WindowState.Minimized, reopenAsFullWindowSetting: true);

        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Show, action);
    }

    [TestMethod]
    public void DetermineToggleAction_VisibleAndSettingDisabled_ReturnsHide()
    {
        var action = QuickSearchWindowController.DetermineToggleAction(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: false);

        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Hide, action);
    }

    [TestMethod]
    public void DetermineToggleAction_VisibleAndSettingEnabled_ReturnsReopenAsFullWindow()
    {
        var action = QuickSearchWindowController.DetermineToggleAction(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: true);

        Assert.AreEqual(QuickSearchWindowController.ToggleAction.ReopenAsFullWindow, action);
    }

    [TestMethod]
    public void DetermineToggleAction_MaximizedAndSettingEnabled_ReturnsReopenAsFullWindow()
    {
        var action = QuickSearchWindowController.DetermineToggleAction(isVisible: true, WindowState.Maximized, reopenAsFullWindowSetting: true);

        Assert.AreEqual(QuickSearchWindowController.ToggleAction.ReopenAsFullWindow, action);
    }
}
