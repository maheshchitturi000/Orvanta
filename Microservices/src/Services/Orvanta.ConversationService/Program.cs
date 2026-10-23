using Orvanta.ConversationService.Data;
using Orvanta.ConversationService.IInterfaces;
using Orvanta.ConversationService.Interfaces;
using Orvanta.ConversationService.Repositories;
using Orvanta.ConversationService.Services;
using Orvanta.Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("conversation-service");

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddOrvantaJwtAuthentication(builder.Configuration);

builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IConversationService, ConversationService>();

var app = builder.Build();

app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    service = "Orvanta.ConversationService",
    status = "healthy"
}));

app.Run();