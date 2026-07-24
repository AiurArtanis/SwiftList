using SwiftList.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace SwiftList.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

// Environment.SetEnvironmentVariable(..., EnvironmentVariableTarget.Process) only affects this test
// process's own environment block, never the real user/machine environment -- safe to set/clear freely.
[TestClass]
public sealed class EnvironmentVariableInstantProviderTests
{
    private const string TestVarName = "SWIFTLISTTESTVARXYZ";

    [TestCleanup]
    public void CleanupTestVar() => Environment.SetEnvironmentVariable(TestVarName, null, EnvironmentVariableTarget.Process);

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() =>
        Assert.IsEmpty(new EnvironmentVariableInstantProvider().GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_KnownFullVariableSyntax_ExpandsToRealValue()
    {
        var result = new EnvironmentVariableInstantProvider().GetInstantResults("%TEMP%").Single();

        Assert.AreEqual(Environment.ExpandEnvironmentVariables("%TEMP%"), result.Title);
    }

    [TestMethod]
    public void GetInstantResults_KnownVariableWithExistingDirectory_OffersExecuteAction()
    {
        // TEMP always points at a real, existing directory on any Windows machine.
        var result = new EnvironmentVariableInstantProvider().GetInstantResults("%TEMP%").Single();

        Assert.AreEqual("Execute", result.ActionType);
    }

    [TestMethod]
    public void GetInstantResults_UnknownVariableFullSyntax_FuzzySearchFindsNothing()
    {
        var results = new EnvironmentVariableInstantProvider().GetInstantResults("%NoSuchSwiftListVarXyz123%").ToList();

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void GetInstantResults_PartialNameWithLeadingPercent_FuzzyModeFindsPrefixMatch()
    {
        Environment.SetEnvironmentVariable(TestVarName, "hello-value", EnvironmentVariableTarget.Process);

        var results = new EnvironmentVariableInstantProvider().GetInstantResults("%SWIFTLISTTESTVARX").ToList();

        Assert.IsTrue(results.Any(r => r.Title == $"%{TestVarName}%"));
    }

    [TestMethod]
    public void GetInstantResults_BarePercent_FuzzyModeListsSomeVariables()
    {
        var results = new EnvironmentVariableInstantProvider().GetInstantResults("%").ToList();

        Assert.IsNotEmpty(results);
        Assert.IsTrue(results.All(r => r.Title.StartsWith('%') && r.Title.EndsWith('%')));
    }

    [TestMethod]
    public void GetInstantResults_NoPercentSign_ReturnsNothing() =>
        Assert.IsEmpty(new EnvironmentVariableInstantProvider().GetInstantResults("just text"));

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() =>
        Assert.IsNull(new EnvironmentVariableInstantProvider().GetHighlightMask("%TEMP%", ""));

    [TestMethod]
    public void GetHighlightMask_NonPercentText_ReturnsAllFalseMask()
    {
        var mask = new EnvironmentVariableInstantProvider().GetHighlightMask("plain text", "%TE");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask.All(b => !b));
    }
}
