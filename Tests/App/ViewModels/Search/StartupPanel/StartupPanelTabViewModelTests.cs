using SwiftList.App.ViewModels.Search.StartupPanel;

namespace SwiftList.App.Tests.ViewModels.Search.StartupPanel;

[TestClass]
public sealed class StartupPanelTabViewModelTests
{
    [TestMethod]
    public void Constructor_SetsLabel()
    {
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.AreEqual("Recent", vm.Label);
    }

    [TestMethod]
    public void IsSelected_DefaultsToFalse()
    {
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.IsFalse(vm.IsSelected);
    }

    [TestMethod]
    public void IsSelected_CanBeSetAndReadBack()
    {
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { }) { IsSelected = true };

        Assert.IsTrue(vm.IsSelected);
    }

    [TestMethod]
    public void CloseCommand_Execute_InvokesOnClose()
    {
        var called = false;
        var vm = new StartupPanelTabViewModel("Recent", () => called = true, () => { });

        vm.CloseCommand.Execute(null);

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void SelectCommand_Execute_InvokesOnSelect()
    {
        var called = false;
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => called = true);

        vm.SelectCommand.Execute(null);

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void Commands_CanExecute_AlwaysTrue()
    {
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.IsTrue(vm.CloseCommand.CanExecute(null));
        Assert.IsTrue(vm.SelectCommand.CanExecute(null));
    }
}
