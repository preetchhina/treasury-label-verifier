using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;
using TreasuryLabelVerifier.Web.Domain;

namespace TreasuryLabelVerifier.Web.Services;

public sealed class OpenAiLabelExtractor : ILabelExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly LabelAnalysisOptions _options;
    private readonly string? _apiKey;

    public OpenAiLabelExtractor(
        HttpClient httpClient,
        IOptions<LabelAnalysisOptions> options,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _apiKey = configuration["OPENAI_API_KEY"];
    }

    public string ProviderName => $"OpenAI {_options.Model}";

    public async Task<LabelExtraction> ExtractAsync(
        ReadOnlyMemory<byte> image,
        string mediaType,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new LabelAnalysisException("The server is missing OPENAI_API_KEY.");
        }

        var dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(image.Span)}";
        var requestBody = new
        {
            model = _options.Model,
            temperature = 0,
            max_tokens = 1600,
            store = false,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "alcohol_label_extraction",
                    strict = true,
                    schema = BuildSchema()
                }
            },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                        You extract visible evidence from U.S. alcohol beverage label artwork for a human compliance reviewer.
                        Transcribe; do not decide legal compliance. Preserve exact capitalization, punctuation, abbreviations, and wording.
                        Confidence is 0 to 1. Set readable false and value null when a field cannot be read.
                        Evidence must identify where/how the text appears without inventing content.
                        Visual booleans must be null when the image does not reliably support an assessment.
                        Never infer country of origin from an address: only extract an explicit country-of-origin statement.
                        Physical millimeter type size is assessable only when the image includes a reliable scale reference.
                        """
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "Extract the label fields and warning typography. Inspect the image itself, including exact warning punctuation and case."
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = dataUrl, detail = "high" }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LabelAnalysisException("The image-analysis request timed out.", true);
        }
        catch (HttpRequestException exception)
        {
            throw new LabelAnalysisException("The image-analysis service could not be reached.", true, exception);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var serviceMessage = TryGetErrorMessage(document.RootElement);
            throw new LabelAnalysisException(
                $"The image-analysis service returned {(int)response.StatusCode}. {serviceMessage}".Trim(),
                (int)response.StatusCode is 408 or 429 or >= 500);
        }

        var content = GetAssistantContent(document.RootElement);
        LabelExtraction? extraction;
        try
        {
            extraction = JsonSerializer.Deserialize<LabelExtraction>(content, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new LabelAnalysisException("The model returned malformed structured output.", false, exception);
        }

        ValidateExtraction(extraction);
        return extraction!;
    }

    private static object BuildSchema()
    {
        object FieldSchema() => new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                value = new { type = new[] { "string", "null" } },
                confidence = new { type = "number", minimum = 0, maximum = 1 },
                evidence = new { type = new[] { "string", "null" } },
                readable = new { type = "boolean" }
            },
            required = new[] { "value", "confidence", "evidence", "readable" }
        };

        var nullableBoolean = new { type = new[] { "boolean", "null" } };
        return new
        {
            type = "object",
            additionalProperties = false,
            properties = new Dictionary<string, object>
            {
                ["brandName"] = FieldSchema(),
                ["classType"] = FieldSchema(),
                ["alcoholContent"] = FieldSchema(),
                ["netContents"] = FieldSchema(),
                ["producerNameAddress"] = FieldSchema(),
                ["countryOfOrigin"] = FieldSchema(),
                ["governmentWarning"] = FieldSchema(),
                ["warningVisuals"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new Dictionary<string, object>
                    {
                        ["headingAllCaps"] = nullableBoolean,
                        ["headingBold"] = nullableBoolean,
                        ["bodyNotBold"] = nullableBoolean,
                        ["continuousParagraph"] = nullableBoolean,
                        ["separateFromOtherText"] = nullableBoolean,
                        ["contrastingBackground"] = nullableBoolean,
                        ["minimumTypeSizeAssessable"] = nullableBoolean,
                        ["visualEvidence"] = new { type = new[] { "string", "null" } }
                    },
                    required = new[]
                    {
                        "headingAllCaps", "headingBold", "bodyNotBold", "continuousParagraph",
                        "separateFromOtherText", "contrastingBackground", "minimumTypeSizeAssessable", "visualEvidence"
                    }
                },
                ["imageQualityNote"] = new { type = new[] { "string", "null" } }
            },
            required = new[]
            {
                "brandName", "classType", "alcoholContent", "netContents", "producerNameAddress",
                "countryOfOrigin", "governmentWarning", "warningVisuals", "imageQualityNote"
            }
        };
    }

    private static string GetAssistantContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new LabelAnalysisException("The model response did not contain a result.");
        }

        var message = choices[0].GetProperty("message");
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
        {
            throw new LabelAnalysisException($"The model declined to analyze this image: {refusal.GetString()}");
        }

        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
        {
            throw new LabelAnalysisException("The model response was incomplete.");
        }

        return content.GetString()!;
    }

    private static string TryGetErrorMessage(JsonElement root) =>
        root.TryGetProperty("error", out var error)
        && error.TryGetProperty("message", out var message)
        && message.ValueKind == JsonValueKind.String
            ? message.GetString() ?? ""
            : "";

    private static void ValidateExtraction(LabelExtraction? extraction)
    {
        if (extraction is null
            || extraction.BrandName is null
            || extraction.ClassType is null
            || extraction.AlcoholContent is null
            || extraction.NetContents is null
            || extraction.ProducerNameAddress is null
            || extraction.CountryOfOrigin is null
            || extraction.GovernmentWarning is null
            || extraction.WarningVisuals is null)
        {
            throw new LabelAnalysisException("The model returned incomplete structured output.");
        }

        var fields = new[]
        {
            extraction.BrandName, extraction.ClassType, extraction.AlcoholContent, extraction.NetContents,
            extraction.ProducerNameAddress, extraction.CountryOfOrigin, extraction.GovernmentWarning
        };

        if (fields.Any(x => x.Confidence is < 0 or > 1)
            || fields.Any(x => x.Readable && string.IsNullOrWhiteSpace(x.Value)))
        {
            throw new LabelAnalysisException("The model returned invalid field values.");
        }
    }
}
