using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.Jobs.Core.Cv;
using Orvanta.Jobs.Core.Domain;
using Orvanta.JobsService.Contracts;
using Orvanta.JobsService.Services;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.JobsService.Controllers;

[Route("api/jobs")]
[Authorize(Policy = "UserOnly")]
public sealed class JobsController(CandidateService candidates, MatchService matches, SourceService sources) : ApiControllerBase
{
    private Guid UserId => User.GetUserId();

    // ---------- Overview ----------

    [HttpGet("summary")]
    public async Task<ActionResult<ResponseCls<JobsSummary>>> Summary(CancellationToken ct) =>
        Respond(ServiceResult<JobsSummary>.Ok(await matches.SummaryAsync(UserId, ct)));

    // ---------- CV & profile ----------

    [HttpGet("profile")]
    public async Task<ActionResult<ResponseCls<ProfileDto>>> GetProfile(CancellationToken ct) =>
        Respond(await candidates.GetProfileAsync(UserId, ct));

    [HttpPut("profile")]
    public async Task<ActionResult<ResponseCls<ProfileDto>>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        Respond(await candidates.UpdateProfileAsync(UserId, request, ct));

    [HttpPost("profile/cv")]
    [RequestSizeLimit(CvTextExtractor.MaxBytes + 64 * 1024)]
    public async Task<ActionResult<ResponseCls<ProfileDto>>> UploadCv(IFormFile? file, CancellationToken ct) =>
        Respond(await candidates.UploadCvAsync(UserId, file, ct));

    /// <summary>The stored CV, used by the Chrome extension to attach it to application forms.</summary>
    [HttpGet("profile/cv")]
    public async Task<IActionResult> DownloadCv(CancellationToken ct)
    {
        var cv = await candidates.GetCvFileAsync(UserId, ct);
        return cv is null
            ? StatusCode(404, ResponseCls.Fail(404, "No CV uploaded."))
            : File(cv.Value.Content, cv.Value.ContentType, cv.Value.FileName);
    }

    // ---------- Preferences ----------

    [HttpGet("preferences")]
    public async Task<ActionResult<ResponseCls<PreferencesDto>>> GetPreferences(CancellationToken ct) =>
        Respond(await candidates.GetPreferencesAsync(UserId, ct));

    [HttpPut("preferences")]
    public async Task<ActionResult<ResponseCls<PreferencesDto>>> SavePreferences(PreferencesDto request, CancellationToken ct) =>
        Respond(await candidates.SavePreferencesAsync(UserId, request, ct));

    [HttpGet("boards/suggested")]
    public ActionResult<ResponseCls<IReadOnlyList<string>>> SuggestedBoards() =>
        Respond(ServiceResult<IReadOnlyList<string>>.Ok(candidates.SuggestedBoards()));

    // ---------- Job search sources (JSearch: LinkedIn · Indeed · Glassdoor; Adzuna) ----------

    [HttpGet("sources")]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<SourceStatusDto>>>> Sources(CancellationToken ct) =>
        Respond(ServiceResult<IReadOnlyList<SourceStatusDto>>.Ok(await sources.ListAsync(UserId, ct)));

    [HttpPut("sources/{name}")]
    public async Task<ActionResult<ResponseCls<SourceStatusDto>>> ConnectSource(string name, ConnectSourceRequest request, CancellationToken ct) =>
        Respond(await sources.ConnectAsync(UserId, name, request.ApiKey, ct));

    [HttpDelete("sources/{name}")]
    public async Task<ActionResult<ResponseCls<object>>> DisconnectSource(string name, CancellationToken ct) =>
        Respond(await sources.DisconnectAsync(UserId, name, ct));

    // ---------- Discovery & matches ----------

    /// <summary>Immediate search (user is waiting). The daily search runs in the background Functions.</summary>
    [HttpPost("discovery/run")]
    public async Task<ActionResult<ResponseCls<DiscoveryResult>>> RunDiscovery(CancellationToken ct) =>
        Respond(await matches.SearchNowAsync(UserId, ct));

    [HttpGet("matches")]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<MatchDto>>>> ListMatches([FromQuery] MatchStatus? status, CancellationToken ct) =>
        Respond(ServiceResult<IReadOnlyList<MatchDto>>.Ok(await matches.ListMatchesAsync(UserId, status ?? MatchStatus.New, ct)));

    [HttpPost("matches/approve")]
    public async Task<ActionResult<ResponseCls<ApproveResult>>> Approve(ApproveMatchesRequest request, CancellationToken ct) =>
        Respond(await matches.ApproveAsync(UserId, request, ct));

    [HttpPost("matches/{matchId:guid}/skip")]
    public async Task<ActionResult<ResponseCls<object>>> Skip(Guid matchId, CancellationToken ct) =>
        Respond(await matches.SkipAsync(UserId, matchId, ct));

    // ---------- Applications ----------

    [HttpGet("applications")]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<ApplicationDto>>>> ListApplications([FromQuery] ApplicationStatus? status, CancellationToken ct) =>
        Respond(ServiceResult<IReadOnlyList<ApplicationDto>>.Ok(await matches.ListApplicationsAsync(UserId, status, ct)));

    [HttpPost("applications/{applicationId:guid}/submitted")]
    public async Task<ActionResult<ResponseCls<object>>> MarkSubmitted(Guid applicationId, CancellationToken ct) =>
        Respond(await matches.MarkSubmittedAsync(UserId, applicationId, ct));

    [HttpPost("applications/{applicationId:guid}/failed")]
    public async Task<ActionResult<ResponseCls<object>>> MarkFailed(Guid applicationId, ReportFailureRequest request, CancellationToken ct) =>
        Respond(await matches.MarkFailedAsync(UserId, applicationId, request.Reason, ct));
}
