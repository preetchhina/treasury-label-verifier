using System.ComponentModel.DataAnnotations;

namespace TreasuryLabelVerifier.Web.Domain;

public static class RegulatoryText
{
    public const string GovernmentWarning =
        "GOVERNMENT WARNING: (1) According to the Surgeon General, women should not drink alcoholic beverages during pregnancy because of the risk of birth defects. (2) Consumption of alcoholic beverages impairs your ability to drive a car or operate machinery, and may cause health problems.";
}

public sealed class LabelApplication
{
    [Required, StringLength(120), Display(Name = "Brand name")]
    public string BrandName { get; set; } = "";

    [Required, StringLength(160), Display(Name = "Class / type")]
    public string ClassType { get; set; } = "";

    [Required, StringLength(80), Display(Name = "Alcohol content")]
    public string AlcoholContent { get; set; } = "";

    [Required, StringLength(80), Display(Name = "Net contents")]
    public string NetContents { get; set; } = "";

    [StringLength(240), Display(Name = "Producer / bottler")]
    public string? ProducerNameAddress { get; set; }

    [StringLength(100), Display(Name = "Country of origin")]
    public string? CountryOfOrigin { get; set; }

    [Required, Display(Name = "Beverage category")]
    public BeverageCategory BeverageCategory { get; set; } = BeverageCategory.DistilledSpirits;
}

public enum BeverageCategory
{
    DistilledSpirits,
    Wine,
    MaltBeverage
}

public sealed record ExtractedField(
    string? Value,
    double Confidence,
    string? Evidence,
    bool Readable);

public sealed record WarningVisualAssessment(
    bool? HeadingAllCaps,
    bool? HeadingBold,
    bool? BodyNotBold,
    bool? ContinuousParagraph,
    bool? SeparateFromOtherText,
    bool? ContrastingBackground,
    bool? MinimumTypeSizeAssessable,
    string? VisualEvidence);

public sealed record LabelExtraction(
    ExtractedField BrandName,
    ExtractedField ClassType,
    ExtractedField AlcoholContent,
    ExtractedField NetContents,
    ExtractedField ProducerNameAddress,
    ExtractedField CountryOfOrigin,
    ExtractedField GovernmentWarning,
    WarningVisualAssessment WarningVisuals,
    string? ImageQualityNote);

public enum ComparisonStatus
{
    ExactMatch,
    FormattingDifference,
    Mismatch,
    Unreadable,
    LowConfidence,
    NotProvided
}

public sealed record FieldComparison(
    string Field,
    string? Expected,
    string? Observed,
    ComparisonStatus Status,
    double Confidence,
    string Reason,
    string? Evidence);

public enum ReviewSeverity
{
    Pass,
    Review,
    Mismatch
}

public sealed record VisualCheck(string Requirement, ReviewSeverity Severity, string Reason);

public sealed record VerificationResult(
    string FileName,
    ReviewSeverity OverallSeverity,
    string OverallTitle,
    IReadOnlyList<FieldComparison> Fields,
    IReadOnlyList<VisualCheck> WarningVisualChecks,
    string? ImageQualityNote,
    long ElapsedMilliseconds,
    string ProviderName);
