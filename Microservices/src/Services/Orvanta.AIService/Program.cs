using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Orvanta.Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("ai-service");

builder.Services.AddOpenApi();
builder.Services.AddOrvantaJwtAuthentication(builder.Configuration);

// "ollama": a local Ollama server (development).
// "openai": any OpenAI-compatible chat API, e.g. Groq or Google Gemini free tiers (production).
var llm = builder.Configuration.GetSection("Llm").Get<LlmSettings>() ?? new();

builder.Services.AddHttpClient("Llm", client =>
{
    client.BaseAddress = new Uri(llm.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(llm.TimeoutSeconds);
    if (!string.IsNullOrWhiteSpace(llm.ApiKey))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
    }
});

var app = builder.Build();

app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "Orvanta.AIService",
    status = "healthy"
}));

app.MapPost("/api/chat", async (
    ChatRequest request,
    IHttpClientFactory factory,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new
        {
            error = "Please enter a message."
        });
    }

    var openAi = llm.IsOpenAiCompatible;

    try
    {
        var client = factory.CreateClient("Llm");

        var payload = new
        {
            model = llm.Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are Orvanta AI, a helpful assistant. " +
                              "Answer clearly and honestly."
                },
                new
                {
                    role = "user",
                    content = request.Message
                }
            },
            stream = false
        };

        using var response = await client.PostAsJsonAsync(
            openAi ? "chat/completions" : "api/chat", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(
                title: "The AI model request failed",
                statusCode: StatusCodes.Status502BadGateway);
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        // Ollama: { message: { content } }. OpenAI-compatible: { choices: [ { message: { content } } ] }.
        var message = openAi
            ? json.RootElement.GetProperty("choices")[0].GetProperty("message")
            : json.RootElement.GetProperty("message");

        var reply = message.GetProperty("content").GetString();

        return Results.Ok(new
        {
            reply = reply ?? "No response generated."
        });
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            title: "Cannot connect to the AI model",
            detail: $"Check that the model server at {llm.BaseUrl} is reachable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireAuthorization("UserOnly");

app.Run();

public sealed record ChatRequest(string Message);

public sealed class LlmSettings
{
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434/";
    public string Model { get; set; } = "qwen3:4b";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 300;

    public bool IsOpenAiCompatible => Provider.Equals("openai", StringComparison.OrdinalIgnoreCase);
}
