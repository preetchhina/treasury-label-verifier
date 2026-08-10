using TreasuryLabelVerifier.Web.Domain;
using TreasuryLabelVerifier.Web.Services;

namespace TreasuryLabelVerifier.Tests;

public sealed class OcrTextInterpreterTests
{
    private readonly OcrTextInterpreter _interpreter = new();

    [Fact]
    public void Interpret_MapsVisibleOcrLinesWithoutInventingApplicationText()
    {
        var lines = Lines(
            "HANDCRAFTED IN KENTUCKY",
            "OLD TOM",
            "DISTILLERY",
            "Kentucky Straight",
            "Bourbon Whiskey",
            "45% Alc./Vol.",
            "750 mL",
            "(90 Proof)",
            "Bottled by Old Tom Distillery, Frankfort, Kentucky",
            "GOVERNMENT WARNING: (1) According to the Surgeon General,",
            "women should not drink alcoholic beverages during pregnancy because of",
            "the risk of birth defects. (2) Consumption of alcoholic beverages impairs",
            "your ability to drive a car or operate machinery, and may cause health",
            "problems.");

        var result = _interpreter.Interpret(lines, Application());

        Assert.Equal("OLD TOM DISTILLERY", result.BrandName.Value);
        Assert.Equal("Kentucky Straight Bourbon Whiskey", result.ClassType.Value);
        Assert.Equal("45% Alc./Vol. (90 Proof)", result.AlcoholContent.Value);
        Assert.Equal("750 mL", result.NetContents.Value);
        Assert.Equal(RegulatoryText.GovernmentWarning, result.GovernmentWarning.Value);
        Assert.True(result.WarningVisuals.HeadingAllCaps);
        Assert.Null(result.WarningVisuals.HeadingBold);
    }

    [Fact]
    public void Interpret_MissingWarningIsUnreadableNotAFalseMismatch()
    {
        var result = _interpreter.Interpret(
            Lines("OLD TOM DISTILLERY", "Kentucky Straight Bourbon Whiskey", "45% Alc./Vol.", "750 mL"),
            Application());

        Assert.False(result.GovernmentWarning.Readable);
        Assert.Null(result.GovernmentWarning.Value);
        Assert.Contains("did not find", result.GovernmentWarning.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseTsv_GroupsWordsIntoLinesAndNormalizesConfidence()
    {
        const string tsv = """
            level	page_num	block_num	par_num	line_num	word_num	left	top	width	height	conf	text
            5	1	1	1	1	1	10	20	50	10	96.0	OLD
            5	1	1	1	1	2	65	20	50	10	94.0	TOM
            5	1	1	1	2	1	10	40	80	10	90.0	DISTILLERY
            """;

        var lines = TesseractLabelExtractor.ParseTsv(tsv);

        Assert.Collection(
            lines,
            first =>
            {
                Assert.Equal("OLD TOM", first.Text);
                Assert.Equal(0.95, first.Confidence, 3);
            },
            second => Assert.Equal("DISTILLERY", second.Text));
    }

    private static LabelApplication Application() => new()
    {
        BrandName = "Old Tom Distillery",
        ClassType = "Kentucky Straight Bourbon Whiskey",
        AlcoholContent = "45% Alc./Vol. (90 Proof)",
        NetContents = "750 mL",
        ProducerNameAddress = "Bottled by Old Tom Distillery, Frankfort, Kentucky"
    };

    private static IReadOnlyList<OcrLine> Lines(params string[] text) =>
        text.Select((value, index) => new OcrLine(value, 0.95, index * 20)).ToArray();
}
