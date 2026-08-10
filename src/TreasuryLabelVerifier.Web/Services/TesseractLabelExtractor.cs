using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;
using TreasuryLabelVerifier.Web.Domain;

namespace TreasuryLabelVerifier.Web.Services;

public sealed class TesseractLabelExtractor(
    IOptions<LabelAnalysisOptions> options,
    OcrTextInterpreter interpreter,
    ILogger<TesseractLabelExtractor> logger) : ILabelExtractor
{
    private readonly LabelAnalysisOptions _options = options.Value;

    public string ProviderName => "Local Tesseract OCR";

    public async Task<LabelExtraction> ExtractAsync(
        ReadOnlyMemory<byte> image,
        string mediaType,
        string fileName,
        LabelApplication application,
        CancellationToken cancellationToken)
    {
        var tsv = await RunTesseractAsync(image, cancellationToken);
        return interpreter.Interpret(ParseTsv(tsv), application);
    }

    private async Task<string> RunTesseractAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.TesseractPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("stdin");
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("eng");
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add("11");
        startInfo.ArgumentList.Add("tsv");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new LabelAnalysisException("The local OCR process could not be started.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new LabelAnalysisException(
                "Local OCR is not installed. Install Tesseract 5 or run the provided Docker image.",
                false,
                exception);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(image, cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Tesseract exited with code {ExitCode}: {Error}", process.ExitCode, error.Trim());
            throw new LabelAnalysisException(
                "Local OCR could not read this image. Confirm it is a valid, non-corrupted PNG, JPEG, or WebP image.");
        }

        return output;
    }

    public static IReadOnlyList<OcrLine> ParseTsv(string tsv)
    {
        var words = new List<OcrWord>();
        foreach (var row in tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var columns = row.TrimEnd('\r').Split('\t', 12);
            if (columns.Length < 12
                || columns[0] != "5"
                || string.IsNullOrWhiteSpace(columns[11])
                || !int.TryParse(columns[1], out var page)
                || !int.TryParse(columns[2], out var block)
                || !int.TryParse(columns[3], out var paragraph)
                || !int.TryParse(columns[4], out var line)
                || !int.TryParse(columns[7], out var top)
                || !double.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                || confidence < 0)
            {
                continue;
            }

            words.Add(new OcrWord(page, block, paragraph, line, top, confidence / 100d, columns[11].Trim()));
        }

        return words
            .GroupBy(word => (word.Page, word.Block, word.Paragraph, word.Line))
            .Select(group => new OcrLine(
                string.Join(' ', group.Select(word => word.Text)),
                group.Average(word => word.Confidence),
                group.Min(word => word.Top)))
            .OrderBy(line => line.Top)
            .ToArray();
    }

    private sealed record OcrWord(
        int Page,
        int Block,
        int Paragraph,
        int Line,
        int Top,
        double Confidence,
        string Text);
}
