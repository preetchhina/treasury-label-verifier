using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TreasuryLabelVerifier.Web.Domain;

namespace TreasuryLabelVerifier.Web.Services;

public sealed partial class ComparisonEngine
{
    private const double LowConfidenceThreshold = 0.70;

    public VerificationResult Compare(
        string fileName,
        LabelApplication application,
        LabelExtraction extraction,
        long elapsedMilliseconds,
        string providerName)
    {
        var fields = new List<FieldComparison>
        {
            CompareText("Brand name", application.BrandName, extraction.BrandName),
            CompareText("Class / type", application.ClassType, extraction.ClassType),
            CompareAlcohol(application.AlcoholContent, extraction.AlcoholContent),
            CompareNetContents(application.NetContents, extraction.NetContents),
            CompareOptional("Producer / bottler", application.ProducerNameAddress, extraction.ProducerNameAddress),
            CompareOptional("Country of origin", application.CountryOfOrigin, extraction.CountryOfOrigin),
            CompareWarning(extraction.GovernmentWarning)
        };

        var visualChecks = CompareWarningVisuals(extraction.WarningVisuals);
        var hasMismatch = fields.Any(x => x.Status == ComparisonStatus.Mismatch)
            || visualChecks.Any(x => x.Severity == ReviewSeverity.Mismatch);
        var needsReview = fields.Any(x => x.Status is ComparisonStatus.Unreadable or ComparisonStatus.LowConfidence)
            || visualChecks.Any(x => x.Severity == ReviewSeverity.Review);

        var severity = hasMismatch
            ? ReviewSeverity.Mismatch
            : needsReview ? ReviewSeverity.Review : ReviewSeverity.Pass;
        var title = severity switch
        {
            ReviewSeverity.Mismatch => "Mismatch detected",
            ReviewSeverity.Review => "Human review needed",
            _ => "No mismatches detected"
        };

        return new VerificationResult(
            fileName,
            severity,
            title,
            fields,
            visualChecks,
            extraction.ImageQualityNote,
            elapsedMilliseconds,
            providerName);
    }

    private static FieldComparison CompareText(string field, string expected, ExtractedField observed)
    {
        if (TryReadabilityResult(field, expected, observed, out var result))
        {
            return result;
        }

        if (string.Equals(expected.Trim(), observed.Value!.Trim(), StringComparison.Ordinal))
        {
            return Build(field, expected, observed, ComparisonStatus.ExactMatch, "Exact text match.");
        }

        if (NormalizeLoose(expected) == NormalizeLoose(observed.Value!))
        {
            return Build(field, expected, observed, ComparisonStatus.FormattingDifference,
                "The words match; only capitalization, punctuation, or spacing differs.");
        }

        return Build(field, expected, observed, ComparisonStatus.Mismatch,
            "The label text is meaningfully different from the application.");
    }

    private static FieldComparison CompareOptional(string field, string? expected, ExtractedField observed)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return Build(field, null, observed, ComparisonStatus.NotProvided,
                "No application value was supplied, so no comparison was made.");
        }

        return CompareText(field, expected, observed);
    }

    private static FieldComparison CompareAlcohol(string expected, ExtractedField observed)
    {
        const string field = "Alcohol content";
        if (TryReadabilityResult(field, expected, observed, out var result))
        {
            return result;
        }

        if (string.Equals(expected.Trim(), observed.Value!.Trim(), StringComparison.Ordinal))
        {
            return Build(field, expected, observed, ComparisonStatus.ExactMatch, "Exact alcohol-content match.");
        }

        var expectedAbv = ParseAbv(expected);
        var observedAbv = ParseAbv(observed.Value!);
        if (expectedAbv is not null && observedAbv is not null && expectedAbv == observedAbv)
        {
            var expectedProof = ParseProof(expected);
            var observedProof = ParseProof(observed.Value!);
            if (expectedProof is not null && observedProof is not null && expectedProof != observedProof)
            {
                return Build(field, expected, observed, ComparisonStatus.Mismatch,
                    $"ABV matches, but proof differs ({expectedProof:0.##} expected vs. {observedProof:0.##} observed).");
            }

            return Build(field, expected, observed, ComparisonStatus.FormattingDifference,
                "The numeric alcohol by volume matches; wording or optional proof presentation differs.");
        }

        return Build(field, expected, observed, ComparisonStatus.Mismatch,
            "The alcohol-by-volume values do not match or could not be compared reliably.");
    }

    private static FieldComparison CompareNetContents(string expected, ExtractedField observed)
    {
        const string field = "Net contents";
        if (TryReadabilityResult(field, expected, observed, out var result))
        {
            return result;
        }

        if (string.Equals(expected.Trim(), observed.Value!.Trim(), StringComparison.Ordinal))
        {
            return Build(field, expected, observed, ComparisonStatus.ExactMatch, "Exact net-contents match.");
        }

        var expectedMl = ParseMilliliters(expected);
        var observedMl = ParseMilliliters(observed.Value!);
        if (expectedMl is not null && observedMl is not null && Math.Abs(expectedMl.Value - observedMl.Value) < 0.01m)
        {
            return Build(field, expected, observed, ComparisonStatus.FormattingDifference,
                "The quantities are equivalent after normalizing liters and milliliters.");
        }

        return Build(field, expected, observed, ComparisonStatus.Mismatch,
            "The net quantity on the label differs from the application.");
    }

    private static FieldComparison CompareWarning(ExtractedField observed)
    {
        const string field = "Government warning wording";
        var expected = RegulatoryText.GovernmentWarning;
        if (TryReadabilityResult(field, expected, observed, out var result))
        {
            return result;
        }

        var collapsedExpected = CollapseWhitespace(expected);
        var collapsedObserved = CollapseWhitespace(observed.Value!);
        if (collapsedExpected == collapsedObserved)
        {
            return Build(field, expected, observed, ComparisonStatus.ExactMatch,
                "Required wording, capitalization, and punctuation match exactly.");
        }

        if (string.Equals(collapsedExpected, collapsedObserved, StringComparison.OrdinalIgnoreCase))
        {
            return Build(field, expected, observed, ComparisonStatus.Mismatch,
                "The words and punctuation match, but required capitalization differs.");
        }

        if (NormalizeWords(expected) == NormalizeWords(observed.Value!))
        {
            return Build(field, expected, observed, ComparisonStatus.Mismatch,
                "The words match, but required punctuation and/or capitalization differs.");
        }

        return Build(field, expected, observed, ComparisonStatus.Mismatch,
            "Required warning wording is missing, incomplete, or changed.");
    }

    private static IReadOnlyList<VisualCheck> CompareWarningVisuals(WarningVisualAssessment visual) =>
    [
        BoolCheck("Heading is all caps", visual.HeadingAllCaps, "The words “GOVERNMENT WARNING” appear in capital letters.", "Required all-caps heading was not detected."),
        BoolCheck("Heading is bold", visual.HeadingBold, "The warning heading appears bold.", "Required bold heading was not detected."),
        BoolCheck("Body is not bold", visual.BodyNotBold, "The warning body does not appear bold.", "The body appears bold; only the heading may be bold."),
        BoolCheck("Continuous paragraph", visual.ContinuousParagraph, "The warning appears as one continuous paragraph.", "The warning does not appear as one continuous paragraph."),
        BoolCheck("Separate from other text", visual.SeparateFromOtherText, "The warning appears separate and apart.", "The warning does not appear separate from other label text."),
        BoolCheck("Contrasting background", visual.ContrastingBackground, "The text/background contrast appears adequate.", "Adequate contrast was not detected."),
        visual.MinimumTypeSizeAssessable == true
            ? new VisualCheck("Minimum physical type size", ReviewSeverity.Pass, "The image includes enough scale information for a size assessment.")
            : new VisualCheck("Minimum physical type size", ReviewSeverity.Review, "Cannot prove millimeter type size from pixels without reliable physical scale; measure during human review.")
    ];

    private static VisualCheck BoolCheck(string requirement, bool? value, string pass, string fail) => value switch
    {
        true => new VisualCheck(requirement, ReviewSeverity.Pass, pass),
        false => new VisualCheck(requirement, ReviewSeverity.Mismatch, fail),
        null => new VisualCheck(requirement, ReviewSeverity.Review, "The image/model could not support a reliable assessment.")
    };

    private static bool TryReadabilityResult(
        string field,
        string? expected,
        ExtractedField observed,
        out FieldComparison result)
    {
        if (!observed.Readable || string.IsNullOrWhiteSpace(observed.Value))
        {
            result = Build(field, expected, observed, ComparisonStatus.Unreadable,
                "The field was missing or not readable enough to compare.");
            return true;
        }

        if (observed.Confidence < LowConfidenceThreshold)
        {
            result = Build(field, expected, observed, ComparisonStatus.LowConfidence,
                $"Extraction confidence ({observed.Confidence:P0}) is below the 70% review threshold.");
            return true;
        }

        result = null!;
        return false;
    }

    private static FieldComparison Build(
        string field,
        string? expected,
        ExtractedField observed,
        ComparisonStatus status,
        string reason) =>
        new(field, expected, observed.Value, status, observed.Confidence, reason, observed.Evidence);

    private static string NormalizeLoose(string value) => NormalizeWords(value);

    private static string NormalizeWords(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string CollapseWhitespace(string value) => WhitespaceRegex().Replace(value.Trim(), " ");

    private static decimal? ParseAbv(string value)
    {
        var match = AbvRegex().Match(value);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static decimal? ParseProof(string value)
    {
        var match = ProofRegex().Match(value);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static decimal? ParseMilliliters(string value)
    {
        var match = VolumeRegex().Match(value);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var unit = match.Groups[2].Value.ToLowerInvariant();
        return unit.StartsWith('l') ? number * 1000m : number;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?i)(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex AbvRegex();

    [GeneratedRegex(@"(?i)(\d+(?:\.\d+)?)\s*proof\b")]
    private static partial Regex ProofRegex();

    [GeneratedRegex(@"(?i)(\d+(?:\.\d+)?)\s*(ml|millilit(?:er|re)s?|l|lit(?:er|re)s?)\b")]
    private static partial Regex VolumeRegex();
}
