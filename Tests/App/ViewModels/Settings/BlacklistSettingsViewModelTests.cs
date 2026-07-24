using SwiftList.Core;
using SwiftList.App.ViewModels.Settings;

namespace SwiftList.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class BlacklistSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsExistingBlacklistedProcesses()
    {
        var settings = new UserSettings { BlacklistedProcesses = new List<string> { "explorer.exe", "notepad.exe" } };

        var vm = new BlacklistSettingsViewModel(settings);

        Assert.HasCount(2, vm.BlacklistedProcesses);
        Assert.AreEqual("explorer.exe" + Environment.NewLine + "notepad.exe", vm.BlacklistText);
    }

    [TestMethod]
    public void Constructor_SkipsBlankEntries()
    {
        var settings = new UserSettings { BlacklistedProcesses = new List<string> { "a.exe", "  ", "" } };

        var vm = new BlacklistSettingsViewModel(settings);

        Assert.HasCount(1, vm.BlacklistedProcesses);
    }

    [TestMethod]
    public void AddProcessCommand_CanExecute_FalseWhenNewProcessNameBlank()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings());

        Assert.IsFalse(vm.AddProcessCommand.CanExecute(null));

        vm.NewProcessName = "a.exe";

        Assert.IsTrue(vm.AddProcessCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddProcessCommand_Execute_AddsTrimmedUnquotedNameAndClearsInput()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings()) { NewProcessName = "  \"chrome.exe\"  " };

        vm.AddProcessCommand.Execute(null);

        Assert.AreEqual("chrome.exe", vm.BlacklistedProcesses[0].Value);
        Assert.AreEqual("", vm.NewProcessName);
    }

    [TestMethod]
    public void AddProcessCommand_Execute_DuplicateNameCaseInsensitive_IsNotAddedTwice()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings()) { NewProcessName = "chrome.exe" };
        vm.AddProcessCommand.Execute(null);
        vm.NewProcessName = "CHROME.EXE";

        vm.AddProcessCommand.Execute(null);

        Assert.HasCount(1, vm.BlacklistedProcesses);
    }

    [TestMethod]
    public void RemoveProcessCommand_Execute_RemovesItemAndRefreshesText()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings()) { NewProcessName = "a.exe" };
        vm.AddProcessCommand.Execute(null);
        var item = vm.BlacklistedProcesses[0];

        vm.RemoveProcessCommand.Execute(item);

        Assert.IsEmpty(vm.BlacklistedProcesses);
        Assert.AreEqual("", vm.BlacklistText);
    }

    [TestMethod]
    public void EditProcessCommand_Execute_MovesValueBackIntoInputAndRemovesFromList()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings()) { NewProcessName = "a.exe" };
        vm.AddProcessCommand.Execute(null);
        var item = vm.BlacklistedProcesses[0];

        vm.EditProcessCommand.Execute(item);

        Assert.AreEqual("a.exe", vm.NewProcessName);
        Assert.IsEmpty(vm.BlacklistedProcesses);
    }

    [TestMethod]
    public void ApplyTextCommand_Execute_ParsesMultilineTextIntoDistinctTrimmedItems()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings())
        {
            BlacklistText = "a.exe\r\n\"b.exe\"\nA.EXE\n  \n"
        };

        vm.ApplyTextCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "a.exe", "b.exe" }, vm.BlacklistedProcesses.Select(x => x.Value).ToList());
    }

    [TestMethod]
    public void ExportTextCommand_Execute_RewritesBlacklistTextFromCurrentItems()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings()) { NewProcessName = "a.exe" };
        vm.AddProcessCommand.Execute(null);
        vm.BlacklistText = "stale text";

        vm.ExportTextCommand.Execute(null);

        Assert.AreEqual("a.exe", vm.BlacklistText);
    }

    [TestMethod]
    public void Save_WritesNormalizedListBackToUserSettings()
    {
        var settings = new UserSettings();
        var vm = new BlacklistSettingsViewModel(settings) { BlacklistText = "a.exe\nA.EXE\nb.exe" };

        vm.Save();

        CollectionAssert.AreEqual(new[] { "a.exe", "b.exe" }, settings.BlacklistedProcesses);
    }
}
