using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class DriveMaintenanceHelperTests
{
    [TestMethod]
    public void NormalizeDrive_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, DriveMaintenanceHelper.NormalizeDrive(""));
        Assert.AreEqual(string.Empty, DriveMaintenanceHelper.NormalizeDrive("   "));
    }

    [TestMethod]
    [DataRow("d", "D")]
    [DataRow("D:", "D")]
    [DataRow(@"D:\", "D")]
    [DataRow("  d  ", "D")]
    public void NormalizeDrive_VariousFormats_NormalizeToUppercaseLetter(string input, string expected) => Assert.AreEqual(expected, DriveMaintenanceHelper.NormalizeDrive(input));
}
