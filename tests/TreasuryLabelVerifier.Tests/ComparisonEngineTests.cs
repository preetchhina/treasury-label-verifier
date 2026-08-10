using TreasuryLabelVerifier.Web.Domain;
using TreasuryLabelVerifier.Web.Services;

namespace TreasuryLabelVerifier.Tests;

public sealed class ComparisonEngineTests
{
    private readonly ComparisonEngine _engine = new();

    [Fact]
    public void CaseAndPunctuationOnlyBrandDifference_IsFormattingDifference()
    {
        var result = Compare(Extraction(brand: "STONE'S THROW"), Application(brand: "Stone’s Throw"));

        var brand = Assert.Single(result.Fields, x => x.Field == "Brand name");
        Assert.Equal(ComparisonStatus.FormattingDifference, brand.Status);
        Assert.Equal(ReviewSeverity.Review, result.OverallSeverity); // physical type size remains a human check
    }

    [Fact]
    public void DifferentAlcoholByVolume_IsMismatch()
    {
        var result = Compare(Extraction(alcohol: "40% Alc. by Vol. (80 Proof)"));

        var alcohol = Assert.Single(result.Fields, x => x.Field == "Alcohol content");
        Assert.Equal(ComparisonStatus.Mismatch, alcohol.Status);
        Assert.Equal(ReviewSeverity.Mismatch, result.OverallSeverity);
    }

    [Fact]
    public void EquivalentVolumeUnits_AreFormattingDifference()
    {
        var result = Compare(Extraction(netContents: "0.75 L"));

        var netContents = Assert.Single(result.Fields, x => x.Field == "Net contents");
        Assert.Equal(ComparisonStatus.FormattingDifference, netContents.Status);
    }

    [Fact]
    public void WarningCapitalizationDifference_IsMismatch()
    {
        var changed = RegulatoryText.GovernmentWarning.Replace("GOVERNMENT WARNING", "Government Warning");
        var result = Compare(Extraction(warning: changed));

        var warning = Assert.Single(result.Fields, x => x.Field == "Government warning wording");
        Assert.Equal(ComparisonStatus.Mismatch, warning.Status);
        Assert.Contains("capitalization", warning.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningPunctuationDifference_IsMismatch()
    {
        var changed = RegulatoryText.GovernmentWarning.Replace("machinery,", "machinery");
        var result = Compare(Extraction(warning: changed));

        var warning = Assert.Single(result.Fields, x => x.Field == "Government warning wording");
        Assert.Equal(ComparisonStatus.Mismatch, warning.Status);
        Assert.Contains("punctuation", warning.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowConfidenceField_IsRoutedToHumanReview()
    {
        var extraction = Extraction() with
        {
            BrandName = new ExtractedField("Old Tom Distillery", 0.55, "Glare over brand", true)
        };
        var result = Compare(extraction);

        var brand = Assert.Single(result.Fields, x => x.Field == "Brand name");
        Assert.Equal(ComparisonStatus.LowConfidence, brand.Status);
        Assert.Equal(ReviewSeverity.Review, result.OverallSeverity);
    }

    [Fact]
    public void UnreadableField_IsNotTreatedAsMismatch()
    {
        var extraction = Extraction() with
        {
            ClassType = new ExtractedField(null, 0.1, "Text obscured", false)
        };
        var result = Compare(extraction);

        var field = Assert.Single(result.Fields, x => x.Field == "Class / type");
        Assert.Equal(ComparisonStatus.Unreadable, field.Status);
        Assert.Equal(ReviewSeverity.Review, result.OverallSeverity);
    }

    [Fact]
    public void FailedBoldHeadingCheck_IsMismatch()
    {
        var extraction = Extraction() with
        {
            WarningVisuals = Visuals() with { HeadingBold = false }
        };
        var result = Compare(extraction);

        var boldCheck = Assert.Single(result.WarningVisualChecks, x => x.Requirement == "Heading is bold");
        Assert.Equal(ReviewSeverity.Mismatch, boldCheck.Severity);
        Assert.Equal(ReviewSeverity.Mismatch, result.OverallSeverity);
    }

    private VerificationResult Compare(LabelExtraction extraction, LabelApplication? application = null) =>
        _engine.Compare("label.png", application ?? Application(), extraction, 120, "test");

    private static LabelApplication Application(string brand = "Old Tom Distillery") => new()
    {
        BrandName = brand,
        ClassType = "Kentucky Straight Bourbon Whiskey",
        AlcoholContent = "45% Alc./Vol. (90 Proof)",
        NetContents = "750 mL",
        ProducerNameAddress = "Old Tom Distillery, Frankfort, Kentucky",
        CountryOfOrigin = null
    };

    private static LabelExtraction Extraction(
        string brand = "Old Tom Distillery",
        string alcohol = "45% Alc./Vol. (90 Proof)",
        string netContents = "750 mL",
        string? warning = null) => new(
            Field(brand),
            Field("Kentucky Straight Bourbon Whiskey"),
            Field(alcohol),
            Field(netContents),
            Field("Old Tom Distillery, Frankfort, Kentucky"),
            Field("United States"),
            Field(warning ?? RegulatoryText.GovernmentWarning),
            Visuals(),
            "Clear image");

    private static WarningVisualAssessment Visuals() =>
        new(true, true, true, true, true, true, false, "Clear warning panel");

    private static ExtractedField Field(string value) => new(value, 0.99, "Visible text", true);
}
