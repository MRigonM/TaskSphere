namespace TaskSphere.Domain.Entities;

public class Project : BaseEntity<int>
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public int NextTaskNumber { get; set; } = 1;
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public ICollection<Member> Members { get; set; } = new List<Member>();
    public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
    public ICollection<Task> Tasks { get; set; } = new List<Task>();
}