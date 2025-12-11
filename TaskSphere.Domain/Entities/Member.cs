using TaskSphere.Domain.Entities.Identity;

namespace TaskSphere.Domain.Entities;

public class Member : BaseEntity<int>
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
}