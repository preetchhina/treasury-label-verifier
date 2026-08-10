using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;
using TreasuryLabelVerifier.Web.Domain;
using TreasuryLabelVerifier.Web.Services;

namespace TreasuryLabelVerifier.Tests;

public sealed class OpenAiLabelExtractorTests
{
    [Fact]
    public async Task ValidStructuredResponse_IsParsed_AndRequestKeepsKeyOutOfBody()
    {
        var extraction = ValidExtraction();
        var apiEnvelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = JsonSerializer.Serialize(extraction) } } }
        });
        var handler = new StubHandler(apiEnvelope);
        var extractor = CreateExtractor(handler);

        var result = await extractor.ExtractAsync(new byte[] { 1, 2, 3 }, "image/png", "label.png", CancellationToken.None);

        Assert.Equal("Old Tom Distillery", result.BrandName.Value);
        Assert.NotNull(handler.RequestBody);
        Assert.Contains("\"store\":false", handler.RequestBody);
        Assert.Contains("data:image/png;base64,AQID", handler.RequestBody);
        Assert.DoesNotContain("test-secret", handler.RequestBody);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-secret", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task MalformedStructuredResponse_IsRejected()
    {
        const string apiEnvelope = "{\"choices\":[{\"message\":{\"content\":\"not-json\"}}]}";
        var extractor = CreateExtractor(new StubHandler(apiEnvelope));

        var exception = await Assert.ThrowsAsync<LabelAnalysisException>(() =>
            extractor.ExtractAsync(new byte[] { 1, 2, 3 }, "image/png", "label.png", CancellationToken.None));

        Assert.Contains("malformed structured output", exception.Message);
    }

    private static OpenAiLabelExtractor CreateExtractor(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OPENAI_API_KEY"] = "test-secret" })
            .Build();
        return new OpenAiLabelExtractor(client, Options.Create(new LabelAnalysisOptions()), configuration);
    }

    private static LabelExtraction ValidExtraction() => new(
        Field("Old Tom Distillery"),
        Field("Bourbon Whiskey"),
        Field("45% Alc. by Vol."),
        Field("750 mL"),
        Field("Old Tom Distillery, Kentucky"),
        new ExtractedField(null, 0.9, "Not present", false),
        Field(RegulatoryText.GovernmentWarning),
        new WarningVisualAssessment(true, true, true, true, true, true, false, "Visible warning"),
        "Clear image");

    private static ExtractedField Field(string value) => new(value, 0.95, "Visible", true);

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
