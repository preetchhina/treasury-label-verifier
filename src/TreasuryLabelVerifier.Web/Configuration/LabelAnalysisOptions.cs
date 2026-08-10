using System.ComponentModel.DataAnnotations;

namespace TreasuryLabelVerifier.Web.Configuration;

public sealed class LabelAnalysisOptions
{
    public const string SectionName = "LabelAnalysis";

    [Required]
    public string Provider { get; set; } = "LocalOcr";

    [Required]
    public string TesseractPath { get; set; } = "tesseract";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 15;

    [Range(1, 10)]
    public int MaxBatchSize { get; set; } = 5;

    [Range(1, 25)]
    public int MaxFileSizeMb { get; set; } = 10;
}
