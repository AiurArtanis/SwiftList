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

    private static string ZipFor(Architecture arch) => $"SwiftList-Portable{UpdateAssetSelector.SuffixFor(arch)}.zip";

    private static Asset? Pick(Architecture arch, params string[] names)
        => UpdateAssetSelector.SelectPortableZip(Array.ConvertAll(names, n => new Asset(n)), a => a.Name, arch);

    [TestMethod]
    public void X64_TakesTheUnsuffixedAsset_NotWhicheverComesFirst()
    {
        // arm64 listed first on purpose: the old code took [0].
        var picked = Pick(Architecture.X64, ZipFor(Architecture.Arm64), ZipFor(Architecture.X64));

        Assert.IsNotNull(picked);
        Assert.AreEqual("SwiftList-Portable.zip", picked.Name);
    }

    [TestMethod]
    public void Arm64_TakesTheSuffixedAsset()
    {
        var picked = Pick(Architecture.Arm64, ZipFor(Architecture.X64), ZipFor(Architecture.Arm64));

        Assert.IsNotNull(picked);
        Assert.AreEqual("SwiftList-Portable_arm64.zip", picked.Name);
    }

    [TestMethod]
    public void TheX64ZipStaysFirstForInstallsPredatingThisSelector()
    {
        // The one property this whole naming scheme exists to hold. Releases are still polled by
        // installs that predate this class and simply take the first asset whose name ends in ".zip",
        // and the GitHub API returns a release's assets sorted by name in plain byte order -- so
        // whichever zip sorts first is what they download and install unattended.
        //
        // '-' is 0x2D and '.' is 0x2E, so a hyphenated suffix puts the arm64 build first and bricks
        // every one of those x64 installs. '_' is 0x5F and sorts after '.'. This test fails the moment
        // the suffix changes to anything that does not preserve that.
        var x64 = ZipFor(Architecture.X64);
        var assets = new[]
        {
            ZipFor(Architecture.Arm64),
            x64,
            x64 + ".sig",
            ZipFor(Architecture.Arm64) + ".sig",
            "SwiftList-Setup.exe",
            $"SwiftList-Setup{UpdateAssetSelector.SuffixFor(Architecture.Arm64)}.exe",
        };
        Array.Sort(assets, StringComparer.Ordinal);

        var legacyPick = Array.Find(assets, n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(x64, legacyPick);
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
        Assert.IsNull(Pick(Architecture.Arm64, ZipFor(Architecture.X64)));
        Assert.IsNull(Pick(Architecture.X64, ZipFor(Architecture.Arm64)));
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
        Assert.IsNull(Pick(Architecture.Arm64, ZipFor(Architecture.Arm64), "Other_arm64.zip"));
    }

    [TestMethod]
    public void AnArchitectureWithNoBuild_UpdatesNothing()
    {
        // x86 and the rest have no release asset at all; they must not fall back to the x64 one.
        Assert.IsNull(Pick(Architecture.X86, ZipFor(Architecture.X64), ZipFor(Architecture.Arm64)));
        Assert.IsNull(Pick(Architecture.Arm, ZipFor(Architecture.X64)));
    }

    [TestMethod]
    public void NoAssetsAtAll_IsHandled()
    {
        Assert.IsNull(Pick(Architecture.X64));
        Assert.IsNull(UpdateAssetSelector.SelectPortableZip<Asset>(null, a => a.Name, Architecture.X64));
    }
}
