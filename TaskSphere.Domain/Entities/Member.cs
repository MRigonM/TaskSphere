using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Domain.Entities;

public class Member : BaseEntity<int>, ISoftDeletion
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}