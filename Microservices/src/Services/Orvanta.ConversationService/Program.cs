using Orvanta.ConversationService.Data;
using Orvanta.ConversationService.IInterfaces;
using Orvanta.ConversationService.Interfaces;
using Orvanta.ConversationService.Repositories;
using Orvanta.ConversationService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IConversationService, ConversationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    service = "Orvanta.ConversationService",
    status = "healthy"
}));

app.Run();