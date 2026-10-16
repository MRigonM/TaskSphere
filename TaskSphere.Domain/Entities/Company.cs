using TaskSphere.Domain.Entities.Identity;

namespace TaskSphere.Domain.Entities;

public class Company : BaseEntity<Guid>
{
    public string Name { get; set; } = default!;

    /// <summary>
    /// A workspace with exactly one human in it (plus that human's Claude). Set at sign-up and
    /// never changed: there is no upgrade path in v1, and nothing needs one yet.
    /// </summary>
    public bool IsPersonal { get; set; }

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
}