using System.Windows;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.Plugins.WindowGuides;

namespace SwiftList.Plugins.WindowGuides.Tests;

[TestClass]
public sealed class GuideSnapCalculatorTests
{
    [TestMethod]
    public void GetConfigSchema_ProvidesFourSliderControlsWithRequestedDefaults()
    {
        var fields = new WindowGuidesPlugin().GetConfigSchema().Fields;

        Assert.HasCount(4, fields);
        Assert.IsTrue(fields.All(field => field.FieldType == ConfigFieldType.Slider));
        Assert.AreEqual(50, fields.Single(field => field.Key == "GuideOpacity").DefaultValue);
        Assert.AreEqual(50, fields.Single(field => field.Key == "OutlineOpacity").DefaultValue);
    }

    [TestMethod]
    public void Snap_WithinHorizontalThreshold_CentersWindowHorizontally()
    {
        var snapped = GuideSnapCalculator.Snap(new Rect(892, 300, 200, 100), new Point(1000, 500));

        Assert.AreEqual(900d, snapped.Left);
        Assert.AreEqual(300d, snapped.Top);
    }

    [TestMethod]
    public void Snap_WithinVerticalThreshold_CentersWindowVertically()
    {
        var snapped = GuideSnapCalculator.Snap(new Rect(300, 443, 100, 100), new Point(500, 500));

        Assert.AreEqual(300d, snapped.Left);
        Assert.AreEqual(450d, snapped.Top);
    }

    [TestMethod]
    public void Snap_OutsideThreshold_LeavesWindowFreeOnThatAxis()
    {
        var snapped = GuideSnapCalculator.Snap(new Rect(880, 300, 200, 100), new Point(1000, 500));

        Assert.AreEqual(880d, snapped.Left);
        Assert.AreEqual(300d, snapped.Top);
    }
}
