using TaskSphere.Domain.Entities.Identity;

namespace TaskSphere.Domain.Entities;

public class Company : BaseEntity<int>
{
    public string Name { get; set; } = default!;
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
}