namespace SwiftList.Core.Tests.Settings;

[TestClass]
public sealed class HotkeyStringFormatTests
{
    [TestMethod]
    [DataRow("Ctrl", true, "Control")]
    [DataRow("Alt", true, "Alt")]
    [DataRow("Shift", true, "Shift")]
    [DataRow("Win", true, "Win")]
    [DataRow("ctrl", true, "Control")] // case-insensitive
    [DataRow("Ctrl+G", false, "")]
    [DataRow("", false, "")]
    [DataRow(null, false, "")]
    public void IsBareModifier_DetectsBareModifierTokens(string? value, bool expectedIsBare, string expectedModifier)
    {
        var isBare = HotkeyStringFormat.IsBareModifier(value, out var modifier);

        Assert.AreEqual(expectedIsBare, isBare);
        Assert.AreEqual(expectedModifier, modifier);
    }

    [TestMethod]
    public void ParseCombo_ModifierPlusKey_SplitsBoth()
    {
        HotkeyStringFormat.ParseCombo("Ctrl+G", out var modifier, out var key);

        Assert.AreEqual("Control", modifier);
        Assert.AreEqual("G", key);
    }

    [TestMethod]
    public void ParseCombo_NonCtrlModifier_PassesThroughUnchanged()
    {
        HotkeyStringFormat.ParseCombo("Alt+P", out var modifier, out var key);

        Assert.AreEqual("Alt", modifier);
        Assert.AreEqual("P", key);
    }

    [TestMethod]
    public void ParseCombo_BareModifierToken_IsModifierWithEmptyKey()
    {
        HotkeyStringFormat.ParseCombo("Ctrl", out var modifier, out var key);

        Assert.AreEqual("Control", modifier);
        Assert.AreEqual(string.Empty, key);
    }

    [TestMethod]
    public void ParseCombo_BareNonModifierToken_IsKeyWithEmptyModifier()
    {
        HotkeyStringFormat.ParseCombo("P", out var modifier, out var key);

        Assert.AreEqual(string.Empty, modifier);
        Assert.AreEqual("P", key);
    }

    [TestMethod]
    public void ParseCombo_EmptyValue_ReturnsEmptyBoth()
    {
        HotkeyStringFormat.ParseCombo(null, out var modifier, out var key);

        Assert.AreEqual(string.Empty, modifier);
        Assert.AreEqual(string.Empty, key);
    }

    [TestMethod]
    public void FormatCombo_ControlAndKey_FormatsAsCtrlPlusKey() => Assert.AreEqual("Ctrl+G", HotkeyStringFormat.FormatCombo("Control", "G"));

    [TestMethod]
    public void FormatCombo_ModifierOnly_ReturnsBareModifier() => Assert.AreEqual("Ctrl", HotkeyStringFormat.FormatCombo("Control", ""));

    [TestMethod]
    public void FormatCombo_KeyOnly_ReturnsBareKey() => Assert.AreEqual("P", HotkeyStringFormat.FormatCombo("", "P"));

    [TestMethod]
    public void FormatCombo_Empty_ReturnsEmptyString() => Assert.AreEqual(string.Empty, HotkeyStringFormat.FormatCombo("", ""));

    [TestMethod]
    public void ParseCombo_ThenFormatCombo_RoundTrips()
    {
        HotkeyStringFormat.ParseCombo("Ctrl+G", out var modifier, out var key);

        Assert.AreEqual("Ctrl+G", HotkeyStringFormat.FormatCombo(modifier, key));
    }

    [TestMethod]
    [DataRow("Oem1", ";")]
    [DataRow("OemPlus", "=")]
    [DataRow("OemComma", ",")]
    [DataRow("OemPeriod", ".")]
    public void ToDisplayText_OemKey_ShowsSymbol(string oemKey, string expectedSymbol) => Assert.AreEqual(expectedSymbol, HotkeyStringFormat.ToDisplayText(oemKey));

    [TestMethod]
    public void ToDisplayText_ComboWithOemKey_ReplacesOnlyTheKeyPart() => Assert.AreEqual("Ctrl+;", HotkeyStringFormat.ToDisplayText("Ctrl+Oem1"));

    [TestMethod]
    public void ToDisplayText_NonOemKey_IsUnchanged() => Assert.AreEqual("Ctrl+G", HotkeyStringFormat.ToDisplayText("Ctrl+G"));

    [TestMethod]
    public void ToDisplayText_EmptyValue_ReturnsEmpty() => Assert.AreEqual(string.Empty, HotkeyStringFormat.ToDisplayText(string.Empty));
}
