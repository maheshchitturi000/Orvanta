using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orvanta.AuthService.Data;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.AuthService.Controllers;

/// <summary>One row of the user's activity (from AuditLogs).</summary>
public sealed record ActivityItem(long AuditLogId, DateTime OccurredAtUtc, string ServiceName, string Action, string Outcome,
    string? EntityType, string? EntityId);

/// <summary>Dashboard: what the signed-in user has done recently. Users only ever see their own rows.</summary>
[Route("api/auth/activity")]
[Authorize(Policy = "UserOnly")]
public sealed class ActivityController(AuthDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<ActivityItem>>>> Get([FromQuery] int top = 50, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        top = Math.Clamp(top, 1, 200);

        var rows = await db.Database
            .SqlQuery<ActivityItem>($"EXEC dbo.usp_AuditLogs_ListForUser @UserId = {userId}, @Top = {top}")
            .ToListAsync(ct);

        // Stored as UTC; mark it so the JSON carries "Z" and browsers show local time correctly.
        IReadOnlyList<ActivityItem> items = rows
            .Select(r => r with { OccurredAtUtc = DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc) })
            .ToList();

        return Respond(ServiceResult<IReadOnlyList<ActivityItem>>.Ok(items));
    }

    /// <summary>
    /// Dashboard charts: action counts per local day and area. <paramref name="utcOffsetMinutes"/> is the
    /// browser's offset from UTC (IST = 330) so days split at the user's midnight.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<ActivitySummaryRow>>>> Summary(
        [FromQuery] int days = 14, [FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        days = Math.Clamp(days, 1, 90);
        utcOffsetMinutes = Math.Clamp(utcOffsetMinutes, -14 * 60, 14 * 60);

        IReadOnlyList<ActivitySummaryRow> rows = await db.Database
            .SqlQuery<ActivitySummaryRow>(
                $"EXEC dbo.usp_AuditLogs_SummaryForUser @UserId = {userId}, @Days = {days}, @UtcOffsetMinutes = {utcOffsetMinutes}")
            .ToListAsync(ct);

        return Respond(ServiceResult<IReadOnlyList<ActivitySummaryRow>>.Ok(rows));
    }
}

/// <summary>Actions on one local day in one area (Auth, Food, Integrations, Jobs...).</summary>
public sealed record ActivitySummaryRow(DateOnly Day, string Area, int Total, int Failed);
