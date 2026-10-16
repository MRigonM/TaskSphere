using TaskSphere.Domain.Entities.Identity;

namespace TaskSphere.Domain.Entities;

/// <summary>
/// A plain class rather than a BaseEntity, mirroring AuditLog: a revoked row IS the history, so
/// soft-delete fields would mean nothing.
/// </summary>
public class AgentToken
{
    public int Id { get; set; }
    public string AgentUserId { get; set; } = string.Empty;
    public AppUser Agent { get; set; } = null!;

    /// <summary>SHA-256 of the full token. The token itself is never stored.</summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>The first 12 characters (<c>tsk_</c> + 8), so the owner can recognise it.</summary>
    public string Prefix { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
