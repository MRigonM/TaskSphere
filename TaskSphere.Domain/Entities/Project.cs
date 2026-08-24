namespace TaskSphere.Domain.Entities;

public class Project : BaseEntity<int>
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public int NextTaskNumber { get; set; } = 1;

    /// <summary>
    /// Opt-in, per project. When false the merge → Done transition resolves and marks pull
    /// requests as considered but never writes a status.
    /// </summary>
    public bool AutoDoneOnMerge { get; set; } = false;
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public ICollection<Member> Members { get; set; } = new List<Member>();
    public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
    public ICollection<Task> Tasks { get; set; } = new List<Task>();
}