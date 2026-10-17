using Microsoft.AspNetCore.Mvc;
using Orvanta.IntegrationService.Repositories;

namespace Orvanta.IntegrationService.Controllers;

[ApiController]
[Route("api/integrations/external")]
public sealed class ExternalIntegrationsController : ControllerBase
{
    private readonly IExternalIntegrationRepository _repository;

    public ExternalIntegrationsController(
        IExternalIntegrationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        var integrations = await _repository.GetAllAsync(
            cancellationToken);

        return Ok(integrations);
    }
}