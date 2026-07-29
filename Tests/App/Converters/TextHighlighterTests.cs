using SwiftList.App.Converters;

namespace SwiftList.App.Tests.Converters;

/// <summary>
/// What the result list actually lights up for a given query. All paths here are synthetic and sit on the
/// placeholder roots repo rule 15 allows.
/// </summary>
[TestClass]
public class TextHighlighterTests
{
    // Renders the mask as the substrings it marks, which reads far better in a failure than a bool[].
    private static string Marked(string text, string query)
    {
        var mask = TextHighlighter.ComputeMask(text, query);
        Assert.HasCount(text.Length, mask, "the mask must cover the text exactly");

        var parts = new List<string>();
        for (var i = 0; i < mask.Length;)
        {
            if (!mask[i]) { i++; continue; }
            var start = i;
            while (i < mask.Length && mask[i]) i++;
            parts.Add(text[start..i]);
        }
        return string.Join("|", parts);
    }

    [TestMethod]
    public void MarksEveryTermOfAMultiWordQuery()
    {
        // The separator between two marked stretches stays unmarked, hence two runs rather than one.
        Assert.AreEqual("Report", Marked("Q3 Report.docx", "report"));
        Assert.AreEqual("Q3|Report", Marked("Q3 Report.docx", "q3 report"));
        Assert.AreEqual("Q3|Report", Marked("Q3 Report.docx", "report q3"));
    }

    [TestMethod]
    public void MarksTheDriveTheQueryNamed()
    {
        // "t:" is a filter rather than a term -- FzfPattern.Parse folds it into TargetDrive and drops it,
        // so nothing downstream marked it and the one part of the query visible in the Path column
        // stayed dark while every other word lit up.
        Assert.AreEqual("T:", Marked(@"T:\Projects\Report", "t:"));
        Assert.AreEqual("T:|Report", Marked(@"T:\Projects\Report", "t: report"));
    }

    [TestMethod]
    public void LeavesTheNameColumnAloneForADriveFilter()
    {
        // A Windows file name cannot contain a colon, so the drive marking can never reach it.
        Assert.AreEqual("Report", Marked("Q3 Report.docx", "t: report"));
        Assert.AreEqual(string.Empty, Marked("Q3 Report.docx", "t:"));
    }

    [TestMethod]
    public void DoesNotMarkADriveTheResultIsNotOn()
    {
        Assert.AreEqual("Report", Marked(@"Z:\Projects\Report", "t: report"));
    }

    [TestMethod]
    public void APathQueryMarksOnlyItsLastSegment()
    {
        // Deliberate: in path mode the user dictated the location, so restating it in the Path column is
        // noise. Only the segment they are narrowing on is marked, and it lands in the Name column.
        Assert.AreEqual("Report", Marked("Report.docx", @"t:\projects\report"));
        Assert.AreEqual(string.Empty, Marked(@"T:\Projects", @"t:\projects\report"));
    }

    [TestMethod]
    public void MarksNothingForAQueryThatDoesNotMatch()
    {
        Assert.AreEqual(string.Empty, Marked("Q3 Report.docx", "nosuchthing"));
        Assert.AreEqual(string.Empty, Marked(@"T:\Projects\Report", "nosuchthing"));
    }

    [TestMethod]
    public void ToleratesTextShorterThanADrivePrefix()
    {
        Assert.AreEqual(string.Empty, Marked("T", "t:"));
        Assert.AreEqual(string.Empty, Marked(string.Empty, "t:"));
    }
}
