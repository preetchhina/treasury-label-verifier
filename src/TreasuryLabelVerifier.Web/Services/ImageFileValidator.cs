using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;

namespace TreasuryLabelVerifier.Web.Services;

public sealed record ValidatedImage(string FileName, string MediaType, byte[] Bytes);

public sealed class ImageFileValidator(IOptions<LabelAnalysisOptions> options)
{
    private readonly LabelAnalysisOptions _options = options.Value;

    public async Task<ValidatedImage> ValidateAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new LabelAnalysisException($"{file.FileName}: the file is empty.");
        }

        var maximumBytes = _options.MaxFileSizeMb * 1024L * 1024L;
        if (file.Length > maximumBytes)
        {
            throw new LabelAnalysisException($"{file.FileName}: file exceeds the {_options.MaxFileSizeMb} MB limit.");
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var mediaType = DetectMediaType(bytes);
        if (mediaType is null)
        {
            throw new LabelAnalysisException($"{file.FileName}: only genuine PNG, JPEG, or WebP images are accepted.");
        }

        return new ValidatedImage(Path.GetFileName(file.FileName), mediaType, bytes);
    }

    private static string? DetectMediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
