namespace Orvanta.IntegrationService.Models;

public sealed class ExternalIntegrationDto
{
    public long IntegrationId { get; set; }

    public string IntegrationName { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public bool IsConnected { get; set; }

    public DateTime? ConnectedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}