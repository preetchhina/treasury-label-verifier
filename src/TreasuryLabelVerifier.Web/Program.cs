using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TreasuryLabelVerifier.Web.Configuration;
using TreasuryLabelVerifier.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<LabelAnalysisOptions>()
    .Bind(builder.Configuration.GetSection(LabelAnalysisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 55 * 1024 * 1024);
builder.Services.AddRazorPages();
builder.Services.AddSingleton<ComparisonEngine>();
builder.Services.AddSingleton<ImageFileValidator>();
builder.Services.AddSingleton<DemoLabelExtractor>();
builder.Services.AddHttpClient<OpenAiLabelExtractor>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<LabelAnalysisOptions>>().Value;
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddScoped<ILabelExtractor>(services =>
{
    var provider = services.GetRequiredService<IOptions<LabelAnalysisOptions>>().Value.Provider;
    return provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<OpenAiLabelExtractor>()
        : services.GetRequiredService<DemoLabelExtractor>();
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data: blob:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.Headers.CacheControl = "public,max-age=3600"
});
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
