using Microsoft.AspNetCore.Identity;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Domain.Entities.Identity;

public class AppUser : IdentityUser, ISoftDeletion
{
    public string Name { get; set; }
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>
    /// A Claude agent rather than a person. Agents have no password and no email, so they can
    /// never log in through /Account/Login; they authenticate with an AgentToken on /mcp only.
    /// </summary>
    public bool IsAgent { get; set; }

    /// <summary>The human this agent acts for. Null for every human.</summary>
    public string? OwnerUserId { get; set; }
    public AppUser? Owner { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}