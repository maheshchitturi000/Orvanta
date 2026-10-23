
using System.Net.Http.Json;
using System.Text.Json;
using Orvanta.Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("ai-service");

builder.Services.AddOpenApi();

builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri("http://localhost:11434/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();

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

    try
    {
        var client = factory.CreateClient("Ollama");

        var payload = new
        {
            model = "qwen3:4b",
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
            "api/chat", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(
                title : "Ollama request failed",
                statusCode: StatusCodes.Status502BadGateway);
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var reply = json.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return Results.Ok(new
        {
            reply = reply ?? "No response generated."
        });
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            title: "Cannot connect to Ollama",
            detail: "Check that Ollama is running at localhost:11434.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public sealed record ChatRequest(string Message);
