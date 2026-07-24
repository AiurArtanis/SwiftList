namespace SwiftList.Core.Tests;

[TestClass]
public sealed class ExclusionRuleSetTests
{
    private static UserSettings EmptySettings() => new()
    {
        ExcludedPaths = new List<string>(),
        IgnoredPathGlobs = new List<string>(),
        IgnoredPathRegexes = new List<string>()
    };

    [TestMethod]
    public void Empty_ExcludesNothing() => Assert.IsFalse(ExclusionRuleSet.Empty.IsExcludedPath(@"c:\anything\at\all.txt", isDirectory: false));

    [TestMethod]
    public void IsExcludedPath_BlankPath_ReturnsFalse() => Assert.IsFalse(ExclusionRuleSet.Empty.IsExcludedPath("", isDirectory: false));

    [TestMethod]
    public void IsExcludedPath_PathUnderExcludedRoot_IsExcluded()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows.old");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\windows.old\system32\file.dll", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_PathOutsideExcludedRoot_IsNotExcluded()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows.old");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\projects\file.txt", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_GlobMatchOnFileName_IsExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\app\node_modules", isDirectory: true));
    }

    [TestMethod]
    public void IsExcludedPath_GlobMatchOnAncestorDirectory_ExcludesDescendants()
    {
        // A file nested inside an ignored directory is excluded too -- IsExcludedPath walks up through
        // every parent directory, not just the path's own final segment.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\app\node_modules\lodash\index.js", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_NoMatchingGlobOrRoot_IsNotExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\projects\app\src\index.js", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_RegexMatchOnFileName_IsExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathRegexes.Add(@"\.tmp$");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\cache.tmp", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_DotPrefixedGlob_MatchesHiddenStyleFolders()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add(".*");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\.git", isDirectory: true));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\projects\src", isDirectory: true));
    }

    [TestMethod]
    public void IsExcludedPath_ExemptRoot_OverridesExcludedRoot()
    {
        // exemptRoot lets a caller explicitly re-include a path that would otherwise be excluded --
        // e.g. the user manually configured a folder index inside an excluded root.
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\data");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false, exemptRoot: @"c:\data"));
    }

    [TestMethod]
    public void InvalidateCache_DoesNotThrow() => ExclusionRuleSet.InvalidateCache();
}
