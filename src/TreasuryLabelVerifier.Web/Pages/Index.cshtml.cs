using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;
using TreasuryLabelVerifier.Web.Domain;
using TreasuryLabelVerifier.Web.Services;

namespace TreasuryLabelVerifier.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class IndexModel(
    ILabelExtractor extractor,
    ImageFileValidator fileValidator,
    ComparisonEngine comparisonEngine,
    IOptions<LabelAnalysisOptions> options,
    IWebHostEnvironment environment,
    ILogger<IndexModel> logger) : PageModel
{
    private readonly LabelAnalysisOptions _options = options.Value;

    [BindProperty]
    public LabelApplication Application { get; set; } = new();

    [BindProperty]
    [System.ComponentModel.DataAnnotations.Display(Name = "Label images")]
    public List<IFormFile> Uploads { get; set; } = [];

    public List<AnalysisItem> Items { get; } = [];
    public bool UsesLocalOcr => extractor is TesseractLabelExtractor;
    public int MaxBatchSize => _options.MaxBatchSize;
    public int MaxFileSizeMb => _options.MaxFileSizeMb;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAnalyzeAsync(CancellationToken cancellationToken)
    {
        if (Uploads.Count == 0)
        {
            ModelState.AddModelError(nameof(Uploads), "Choose at least one label image.");
        }
        else if (Uploads.Count > _options.MaxBatchSize)
        {
            ModelState.AddModelError(nameof(Uploads), $"Choose no more than {_options.MaxBatchSize} images at once.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await AnalyzeFilesAsync(Uploads, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSampleAsync(CancellationToken cancellationToken)
    {
        await AnalyzeSampleAsync("sample-old-tom.png", cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostIssueSampleAsync(CancellationToken cancellationToken)
    {
        await AnalyzeSampleAsync("sample-warning-issues.png", cancellationToken);
        return Page();
    }

    private async Task AnalyzeSampleAsync(string fileName, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        Application = SampleApplication();
        var path = Path.Combine(environment.WebRootPath, "samples", fileName);
        var bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Uploads", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        await AnalyzeFilesAsync([formFile], cancellationToken);
    }

    private async Task AnalyzeFilesAsync(IEnumerable<IFormFile> files, CancellationToken cancellationToken)
    {
        using var concurrencyGate = new SemaphoreSlim(2, 2);
        var tasks = files.Select(async file =>
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                return await AnalyzeOneAsync(file, cancellationToken);
            }
            finally
            {
                concurrencyGate.Release();
            }
        });
        Items.AddRange(await Task.WhenAll(tasks));
    }

    private async Task<AnalysisItem> AnalyzeOneAsync(IFormFile file, CancellationToken requestCancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var validated = await fileValidator.ValidateAsync(file, requestCancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var extraction = await extractor.ExtractAsync(
                validated.Bytes,
                validated.MediaType,
                validated.FileName,
                Application,
                timeout.Token);
            stopwatch.Stop();
            return new AnalysisItem(
                comparisonEngine.Compare(
                    validated.FileName,
                    Application,
                    extraction,
                    stopwatch.ElapsedMilliseconds,
                    extractor.ProviderName),
                null,
                validated.FileName);
        }
        catch (OperationCanceledException) when (!requestCancellationToken.IsCancellationRequested)
        {
            return new AnalysisItem(null, "Analysis timed out. Try a clearer or smaller image, then retry.", Path.GetFileName(file.FileName));
        }
        catch (LabelAnalysisException exception)
        {
            return new AnalysisItem(null, exception.Message, Path.GetFileName(file.FileName));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected label analysis failure.");
            return new AnalysisItem(null, "This image could not be analyzed. No file was retained; please retry.", Path.GetFileName(file.FileName));
        }
    }

    private static LabelApplication SampleApplication() => new()
    {
        BrandName = "Old Tom Distillery",
        ClassType = "Kentucky Straight Bourbon Whiskey",
        AlcoholContent = "45% Alc./Vol. (90 Proof)",
        NetContents = "750 mL",
        ProducerNameAddress = "Bottled by Old Tom Distillery, Frankfort, Kentucky",
        CountryOfOrigin = null,
        BeverageCategory = BeverageCategory.DistilledSpirits
    };

    public sealed record AnalysisItem(VerificationResult? Result, string? Error, string FileName);
}
