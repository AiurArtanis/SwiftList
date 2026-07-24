namespace SwiftList.Core.Tests.Indexer;

[TestClass]
public sealed class VolumeHelperTests
{
    [TestMethod]
    public void GetVolumeCacheKey_IsDeterministic()
    {
        var identity = new VolumeHelper.VolumeIdentity("NTFS", 0x12345678);

        var a = VolumeHelper.GetVolumeCacheKey(identity);
        var b = VolumeHelper.GetVolumeCacheKey(identity);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void GetVolumeCacheKey_DependsOnlyOnSerialNumber_NotFileSystemType()
    {
        var ntfs = new VolumeHelper.VolumeIdentity("NTFS", 0xAABBCCDD);
        var refs = new VolumeHelper.VolumeIdentity("ReFS", 0xAABBCCDD);

        Assert.AreEqual(VolumeHelper.GetVolumeCacheKey(ntfs), VolumeHelper.GetVolumeCacheKey(refs));
    }

    [TestMethod]
    public void GetVolumeCacheKey_DifferentSerialNumbers_ProduceDifferentKeys()
    {
        var a = new VolumeHelper.VolumeIdentity("NTFS", 1);
        var b = new VolumeHelper.VolumeIdentity("NTFS", 2);

        Assert.AreNotEqual(VolumeHelper.GetVolumeCacheKey(a), VolumeHelper.GetVolumeCacheKey(b));
    }

    [TestMethod]
    public void GetVolumeCacheKey_IsLowercaseHexSha256()
    {
        var key = VolumeHelper.GetVolumeCacheKey(new VolumeHelper.VolumeIdentity("NTFS", 0));

        Assert.AreEqual(64, key.Length);
        Assert.AreEqual(key.ToLowerInvariant(), key);
    }
}
