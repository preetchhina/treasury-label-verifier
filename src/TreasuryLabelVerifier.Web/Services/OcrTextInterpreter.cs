using System.Text;
using System.Text.RegularExpressions;
using TreasuryLabelVerifier.Web.Domain;

namespace TreasuryLabelVerifier.Web.Services;

public sealed record OcrLine(string Text, double Confidence, int Top);

public sealed partial class OcrTextInterpreter
{
    private const double MinimumCandidateScore = 0.42;

    public LabelExtraction Interpret(IReadOnlyList<OcrLine> lines, LabelApplication application)
    {
        var usable = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => line.Top)
            .ToArray();

        if (usable.Length == 0)
        {
            throw new LabelAnalysisException(
                "No readable text was detected. Try a sharper, front-facing image with less glare.");
        }

        var warning = ExtractWarning(usable);
        var averageConfidence = usable.Average(line => line.Confidence);
        bool? headingAllCaps = null;
        if (warning.Readable && WarningHeadingRegex().Match(warning.Value ?? "") is { Success: true } heading)
        {
            headingAllCaps = heading.Value == heading.Value.ToUpperInvariant();
        }

        return new LabelExtraction(
            MatchExpected(application.BrandName, usable, "brand-name text"),
            MatchExpected(application.ClassType, usable, "class/type text"),
            MatchPattern(application.AlcoholContent, usable, AlcoholMarkerRegex(), "alcohol-content text", maximumWindow: 2),
            MatchPattern(application.NetContents, usable, NetContentsMarkerRegex(), "net-contents text", maximumWindow: 1),
            MatchOptional(application.ProducerNameAddress, usable, "producer/bottler text"),
            MatchOptional(application.CountryOfOrigin, usable, "country-of-origin text"),
            warning,
            new WarningVisualAssessment(
                headingAllCaps,
                null,
                null,
                null,
                null,
                null,
                false,
                "Local OCR can assess the warning heading's capitalization, but not reliably prove font weight, layout separation, contrast, or physical type size."),
            $"Local OCR read {usable.Count(line => line.Confidence >= 0.50)} of {usable.Length} text lines at an average {averageConfidence:P0} confidence. Photograph angle, glare, and decorative type can reduce accuracy.");
    }

    private static ExtractedField MatchOptional(string? expected, IReadOnlyList<OcrLine> lines, string evidenceLabel) =>
        string.IsNullOrWhiteSpace(expected)
            ? Missing($"No application value was supplied for {evidenceLabel}.")
            : MatchExpected(expected, lines, evidenceLabel);

    private static ExtractedField MatchPattern(
        string expected,
        IReadOnlyList<OcrLine> lines,
        Regex marker,
        string evidenceLabel,
        int maximumWindow)
    {
        var candidates = lines.Where(line => marker.IsMatch(line.Text)).ToArray();
        return candidates.Length == 0
            ? Missing($"OCR did not find recognizable {evidenceLabel}.")
            : MatchExpected(expected, candidates, evidenceLabel, maximumWindow);
    }

    private static ExtractedField MatchExpected(
        string expected,
        IReadOnlyList<OcrLine> lines,
        string evidenceLabel,
        int maximumWindow = 3)
    {
        Candidate? best = null;
        for (var start = 0; start < lines.Count; start++)
        {
            var text = new StringBuilder();
            var confidences = new List<double>();
            for (var length = 1; length <= maximumWindow && start + length <= lines.Count; length++)
            {
                var line = lines[start + length - 1];
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(line.Text.Trim());
                confidences.Add(line.Confidence);
                var observed = text.ToString();
                if (Normalize(observed).Length > Math.Max(24, Normalize(expected).Length * 3))
                {
                    break;
                }

                var score = Similarity(expected, observed);
                if (best is null || score > best.Score)
                {
                    best = new Candidate(observed, confidences.Average(), score);
                }
            }
        }

        if (best is null || best.Score < MinimumCandidateScore)
        {
            return Missing($"OCR could not locate {evidenceLabel} with enough confidence to compare.");
        }

        var confidence = Math.Clamp(best.OcrConfidence * (0.65 + (0.35 * best.Score)), 0, 1);
        return new ExtractedField(
            best.Text,
            confidence,
            $"OCR line selected as the closest visible {evidenceLabel} (text similarity {best.Score:P0}; OCR confidence {best.OcrConfidence:P0}).",
            true);
    }

    private static ExtractedField ExtractWarning(IReadOnlyList<OcrLine> lines)
    {
        var start = Array.FindIndex(lines.ToArray(), line => WarningStartRegex().IsMatch(line.Text));
        if (start < 0)
        {
            return Missing("OCR did not find a recognizable GOVERNMENT WARNING heading.");
        }

        var selected = new List<OcrLine>();
        for (var index = start; index < lines.Count && selected.Count < 10; index++)
        {
            selected.Add(lines[index]);
            if (WarningEndRegex().IsMatch(string.Join(' ', selected.Select(line => line.Text))))
            {
                break;
            }
        }

        var text = string.Join(' ', selected.Select(line => line.Text.Trim()));
        var hasEnoughText = Normalize(text).Length >= 80;
        return new ExtractedField(
            hasEnoughText ? text : null,
            hasEnoughText ? selected.Average(line => line.Confidence) : 0,
            hasEnoughText
                ? $"OCR transcribed {selected.Count} consecutive line(s) beginning with the government-warning heading."
                : "A warning heading was detected, but the body was not readable enough to compare.",
            hasEnoughText);
    }

    private static ExtractedField Missing(string evidence) => new(null, 0, evidence, false);

    private static double Similarity(string expected, string observed)
    {
        var left = Normalize(expected);
        var right = Normalize(observed);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var distance = LevenshteinDistance(left, right);
        var editSimilarity = 1d - ((double)distance / Math.Max(left.Length, right.Length));
        var expectedTokens = Tokens(expected);
        var observedTokens = Tokens(observed);
        var tokenCoverage = expectedTokens.Count == 0
            ? 0
            : expectedTokens.Count(observedTokens.Contains) / (double)expectedTokens.Count;
        return Math.Clamp((0.65 * editSimilarity) + (0.35 * tokenCoverage), 0, 1);
    }

    private static HashSet<string> Tokens(string value) =>
        TokenRegex().Matches(value.ToUpperInvariant())
            .Select(match => match.Value)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record Candidate(string Text, double OcrConfidence, double Score);

    [GeneratedRegex(@"(?i)(?:\balc(?:ohol)?\.?\s*(?:by|/)\s*vol\b|\balc\.?/vol\b|\bproof\b|\b\d{1,3}(?:\.\d+)?\s*%)")]
    private static partial Regex AlcoholMarkerRegex();

    [GeneratedRegex(@"(?i)\b\d+(?:\.\d+)?\s*(?:m\s*l|ml|millilit(?:er|re)s?|l|lit(?:er|re)s?)\b")]
    private static partial Regex NetContentsMarkerRegex();

    [GeneratedRegex(@"(?i)\bgovernment\s+warning\b")]
    private static partial Regex WarningStartRegex();

    [GeneratedRegex(@"(?i)\bhealth\s+problems\b")]
    private static partial Regex WarningEndRegex();

    [GeneratedRegex(@"(?i)\bgovernment\s+warning\b")]
    private static partial Regex WarningHeadingRegex();

    [GeneratedRegex(@"[A-Z0-9]+")]
    private static partial Regex TokenRegex();
}
