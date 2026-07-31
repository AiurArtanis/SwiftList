using System.Runtime.InteropServices;
using SwiftList.App.Services.Update;

namespace SwiftList.App.Tests.Services.Update;

// Both update paths used to take the first asset ending in ".zip", which was fine while a release
// carried one. Once it carries an arm64 build too, "the first zip" eventually hands an x64 machine an
// arm64 build and installs it unattended, leaving the app unable to start.
[TestClass]
public sealed class UpdateAssetSelectorTests
{
    private sealed record Asset(string Name);

    private static Asset? Pick(Architecture arch, params string[] names)
        => UpdateAssetSelector.SelectPortableZip(Array.ConvertAll(names, n => new Asset(n)), a => a.Name, arch);

    [TestMethod]
    public void X64_TakesTheUnsuffixedAsset_NotWhicheverComesFirst()
    {
        // arm64 listed first on purpose: GitHub does not promise an order, and the old code took [0].
        var picked = Pick(Architecture.X64, "SwiftList-Portable-arm64.zip", "SwiftList-Portable.zip");

        Assert.IsNotNull(picked);
        Assert.AreEqual("SwiftList-Portable.zip", picked.Name);
    }

    [TestMethod]
    public void Arm64_TakesTheSuffixedAsset()
    {
        var picked = Pick(Architecture.Arm64, "SwiftList-Portable.zip", "SwiftList-Portable-arm64.zip");

        Assert.IsNotNull(picked);
        Assert.AreEqual("SwiftList-Portable-arm64.zip", picked.Name);
    }

    [TestMethod]
    public void AReleaseFromBeforeArm64Existed_StillUpdatesOnX64()
    {
        // The whole reason the x64 asset keeps its old unsuffixed name: installs predating the arm64
        // build look for exactly this, and renaming it would strand them.
        var picked = Pick(Architecture.X64, "SwiftList-Portable.zip");

        Assert.IsNotNull(picked);
        Assert.AreEqual("SwiftList-Portable.zip", picked.Name);
    }

    [TestMethod]
    public void AReleaseWithNoMatchingBuild_UpdatesNothing()
    {
        // Not updating is an annoyance. Installing the wrong architecture over a working install is not,
        // and the updater runs unattended, so there is no one to catch it.
        Assert.IsNull(Pick(Architecture.Arm64, "SwiftList-Portable.zip"));
        Assert.IsNull(Pick(Architecture.X64, "SwiftList-Portable-arm64.zip"));
    }

    [TestMethod]
    public void NonZipAssetsAreIgnored()
    {
        var picked = Pick(Architecture.X64, "SwiftList-Setup.exe", "SwiftList-Portable.zip.sig", "SwiftList-Portable.zip");

        Assert.IsNotNull(picked);
        Assert.AreEqual("SwiftList-Portable.zip", picked.Name);
    }

    [TestMethod]
    public void AnAmbiguousReleaseUpdatesNothing()
    {
        // Two assets both claiming to be this architecture's means the naming assumption has broken.
        // Picking one of them is exactly the guess this class exists to avoid.
        Assert.IsNull(Pick(Architecture.X64, "SwiftList-Portable.zip", "SwiftList-Extra.zip"));
        Assert.IsNull(Pick(Architecture.Arm64, "SwiftList-Portable-arm64.zip", "Other-arm64.zip"));
    }

    [TestMethod]
    public void AnArchitectureWithNoBuild_UpdatesNothing()
    {
        // x86 and the rest have no release asset at all; they must not fall back to the x64 one.
        Assert.IsNull(Pick(Architecture.X86, "SwiftList-Portable.zip", "SwiftList-Portable-arm64.zip"));
        Assert.IsNull(Pick(Architecture.Arm, "SwiftList-Portable.zip"));
    }

    [TestMethod]
    public void NoAssetsAtAll_IsHandled()
    {
        Assert.IsNull(Pick(Architecture.X64));
        Assert.IsNull(UpdateAssetSelector.SelectPortableZip<Asset>(null, a => a.Name, Architecture.X64));
    }
}
