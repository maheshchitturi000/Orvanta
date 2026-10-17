using Orvanta.Jobs.Core.Sources;
using Orvanta.JobsService.Contracts;
using Orvanta.Shared.Responses;

namespace Orvanta.JobsService.Services;

/// <summary>Connect / disconnect job search sources such as LinkedIn · Naukri · Indeed (JSearch).</summary>
public sealed class SourceService(IEnumerable<IJobSearchSource> sources, JobSourceCredentials credentials, ILogger<SourceService> logger)
{
    public async Task<IReadOnlyList<SourceStatusDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        var result = new List<SourceStatusDto>();
        foreach (var source in sources)
        {
            var (connected, serverKey, connectedAt) = await credentials.StatusAsync(userId, source.Name, ct);
            result.Add(new SourceStatusDto(source.Name, source.DisplayName, connected, serverKey, connectedAt));
        }
        return result;
    }

    /// <summary>Tests the key with one small search before saving it, so "Connected" always means it works.</summary>
    public async Task<ServiceResult<SourceStatusDto>> ConnectAsync(Guid userId, string name, string apiKey, CancellationToken ct)
    {
        var source = sources.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (source is null) return ServiceResult<SourceStatusDto>.Fail(404, "Unknown job source.");

        var key = apiKey.Trim();
        try
        {
            await source.SearchAsync(new JobSearchQuery("Software Engineer", "India", false), key, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Key check for {Source} failed: {Message}", source.Name, ex.Message);

            // Only blame the key when the provider actually rejected it.
            if (ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase))
                return ServiceResult<SourceStatusDto>.Fail(400,
                    "That key was rejected. Copy the x-rapidapi-key value (without quotes) from RapidAPI → JSearch → Playground, and make sure you subscribed to the Basic (free) plan.");

            if (ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
                return ServiceResult<SourceStatusDto>.Fail(400, "This key works but its monthly quota is used up. Try again next month or upgrade the plan.");

            return ServiceResult<SourceStatusDto>.Fail(502, $"{source.DisplayName} couldn't be reached to check the key ({ex.Message}). Please try again shortly.");
        }

        await credentials.SaveAsync(userId, source.Name, key, ct);
        var (_, _, connectedAt) = await credentials.StatusAsync(userId, source.Name, ct);
        return ServiceResult<SourceStatusDto>.Ok(new SourceStatusDto(source.Name, source.DisplayName, true, false, connectedAt),
            $"{source.DisplayName} connected. Search now to find jobs.");
    }

    public async Task<ServiceResult<object>> DisconnectAsync(Guid userId, string name, CancellationToken ct)
    {
        await credentials.DeleteAsync(userId, name.ToLowerInvariant(), ct);
        return ServiceResult<object>.Ok(null!, "Disconnected.");
    }
}
