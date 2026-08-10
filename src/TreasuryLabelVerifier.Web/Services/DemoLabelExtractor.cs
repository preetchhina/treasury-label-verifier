using TreasuryLabelVerifier.Web.Domain;

namespace TreasuryLabelVerifier.Web.Services;

public sealed class DemoLabelExtractor : ILabelExtractor
{
    public string ProviderName => "Sample fixture";

    public Task<LabelExtraction> ExtractAsync(
        ReadOnlyMemory<byte> image,
        string mediaType,
        string fileName,
        LabelApplication application,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isPassSample = fileName.Equals("sample-old-tom.png", StringComparison.OrdinalIgnoreCase);
        var isIssueSample = fileName.Equals("sample-warning-issues.png", StringComparison.OrdinalIgnoreCase);
        if (!isPassSample && !isIssueSample)
        {
            throw new LabelAnalysisException(
                "This fixture-only provider recognizes the two included samples. Use the local OCR provider for arbitrary images.");
        }

        var warning = isIssueSample
            ? RegulatoryText.GovernmentWarning
                .Replace("GOVERNMENT WARNING", "Government Warning")
                .Replace("machinery,", "machinery")
            : RegulatoryText.GovernmentWarning;
        var extraction = new LabelExtraction(
            Field("OLD TOM DISTILLERY", 0.99, "Large cream serif text at top center."),
            Field("Kentucky Straight Bourbon Whiskey", 0.98, "Centered beneath brand name."),
            Field("45% Alc./Vol. (90 Proof)", 0.99, "Lower-left product information."),
            Field("750 mL", 0.99, "Lower-right product information."),
            Field("Bottled by Old Tom Distillery, Frankfort, Kentucky", 0.97, "Small text above warning box."),
            new ExtractedField(null, 0.99, "No explicit country-of-origin statement appears on this domestic sample.", false),
            Field(warning, 0.99, "Warning panel at bottom of label."),
            new WarningVisualAssessment(!isIssueSample, !isIssueSample, true, true, true, true, false,
                isIssueSample
                    ? "The heading is title case and the same weight as the body."
                    : "The heading is visibly heavier than the body and the panel has strong dark-on-light contrast."),
            "Sharp, front-facing sample artwork with high contrast.");

        return Task.FromResult(extraction);
    }

    private static ExtractedField Field(string value, double confidence, string evidence) =>
        new(value, confidence, evidence, true);
}
