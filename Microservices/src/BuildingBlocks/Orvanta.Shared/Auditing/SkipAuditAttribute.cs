namespace Orvanta.Shared.Auditing;

/// <summary>
/// Excludes a controller or action from <see cref="AuditMiddleware"/>. Use it where the code already writes
/// richer events through <see cref="IAuditLogger"/> (e.g. sign-in), so one action isn't recorded twice.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipAuditAttribute : Attribute;
