using SwiftList.App.Services;
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

// UiMetrics.Scale is shared static state -- these tests save/restore it and run un-parallelized so
// they don't race with anything else in the assembly that happens to read it mid-test.
[TestClass]
[DoNotParallelize]
public sealed class StartupPanelTabViewModelScaleTests
{
    private double _originalScale;

    [TestInitialize]
    public void SaveScale() => _originalScale = UiMetrics.Scale;

    [TestCleanup]
    public void RestoreScale() => UiMetrics.Scale = _originalScale;

    [TestMethod]
    public void ScaledFontSize_AtDefaultScale_MatchesBaseValue()
    {
        UiMetrics.Scale = 1.0;
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.AreEqual(14, vm.ScaledFontSize);
    }

    [TestMethod]
    public void ScaledFontSize_AtLargerScale_ScalesProportionally()
    {
        UiMetrics.Scale = 1.5;
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.AreEqual(21, vm.ScaledFontSize);
    }

    [TestMethod]
    public void ScaledCloseButtonSize_AtLargerScale_ScalesProportionally()
    {
        UiMetrics.Scale = 1.5;
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.AreEqual(24, vm.ScaledCloseButtonSize);
    }

    [TestMethod]
    public void ScaledUnderlineHeight_NeverGoesBelowOnePixel()
    {
        // At the low end of UiMetrics.Scale's own clamp (0.6), 2 * 0.6 = 1.2 rounds to 1 anyway, so this
        // pins the floor explicitly against a future base/clamp-range change rather than relying on that
        // coincidence.
        UiMetrics.Scale = 0.6;
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.IsGreaterThanOrEqualTo(1.0, vm.ScaledUnderlineHeight);
    }

    [TestMethod]
    public void ScaledUnderlineMarginThickness_TopMatchesNegativeUnderlineHeight()
    {
        UiMetrics.Scale = 1.5;
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });

        Assert.AreEqual($"0,{-vm.ScaledUnderlineHeight},0,0", vm.ScaledUnderlineMarginThickness);
    }

    [TestMethod]
    public void RefreshScale_RaisesPropertyChanged()
    {
        var vm = new StartupPanelTabViewModel("Recent", () => { }, () => { });
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.RefreshScale();

        Assert.IsTrue(raised);
    }
}
