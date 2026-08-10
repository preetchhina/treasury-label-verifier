using TreasuryLabelVerifier.Web.Domain;

namespace TreasuryLabelVerifier.Web.Services;

public interface ILabelExtractor
{
    string ProviderName { get; }

    Task<LabelExtraction> ExtractAsync(
        ReadOnlyMemory<byte> image,
        string mediaType,
        string fileName,
        CancellationToken cancellationToken);
}

public sealed class LabelAnalysisException(string message, bool isTransient = false, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsTransient { get; } = isTransient;
}
