using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;
using TreasuryLabelVerifier.Web.Services;

namespace TreasuryLabelVerifier.Tests;

public sealed class ImageFileValidatorTests
{
    private readonly ImageFileValidator _validator = new(Options.Create(new LabelAnalysisOptions()));

    [Fact]
    public async Task RejectsFileWhoseExtensionLiesAboutItsContent()
    {
        var file = FormFile("label.png", "this is not an image"u8.ToArray());

        var exception = await Assert.ThrowsAsync<LabelAnalysisException>(() =>
            _validator.ValidateAsync(file, CancellationToken.None));

        Assert.Contains("genuine PNG", exception.Message);
    }

    [Fact]
    public async Task AcceptsPngByMagicBytes_NotClientContentType()
    {
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        var file = FormFile("label.bin", bytes);

        var result = await _validator.ValidateAsync(file, CancellationToken.None);

        Assert.Equal("image/png", result.MediaType);
        Assert.Equal("label.bin", result.FileName);
    }

    [Fact]
    public async Task RejectsEmptyFile()
    {
        var file = FormFile("empty.jpg", []);

        await Assert.ThrowsAsync<LabelAnalysisException>(() =>
            _validator.ValidateAsync(file, CancellationToken.None));
    }

    private static FormFile FormFile(string name, byte[] bytes) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "Uploads", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };
}
