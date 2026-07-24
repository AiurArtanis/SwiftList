using SwiftList.App.Services.UrlProtocol;

namespace SwiftList.App.Tests.Services.UrlProtocol;

[TestClass]
public sealed class UriRouterTests
{
    [TestMethod]
    public void IsSwiftListUri_SwiftListScheme_ReturnsTrue() => Assert.IsTrue(UriRouter.IsSwiftListUri("swiftlist://search"));

    [TestMethod]
    public void IsSwiftListUri_SchemeIsCaseInsensitive() => Assert.IsTrue(UriRouter.IsSwiftListUri("SwiftList://search"));

    [TestMethod]
    public void IsSwiftListUri_HttpScheme_ReturnsFalse() => Assert.IsFalse(UriRouter.IsSwiftListUri("https://example.com"));

    [TestMethod]
    public void IsSwiftListUri_NullOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(UriRouter.IsSwiftListUri(null));
        Assert.IsFalse(UriRouter.IsSwiftListUri(""));
    }

    [TestMethod]
    public void IsSwiftListUri_RelativeUri_ReturnsFalse() => Assert.IsFalse(UriRouter.IsSwiftListUri("search/foo"));

    [TestMethod]
    public void IsSwiftListUri_MalformedUri_ReturnsFalse() => Assert.IsFalse(UriRouter.IsSwiftListUri("not a uri at all"));

    [TestMethod]
    public void IsSwiftListUri_UriWithPathAndArgs_ReturnsTrue() => Assert.IsTrue(UriRouter.IsSwiftListUri("swiftlist://settings/page/Index"));
}
