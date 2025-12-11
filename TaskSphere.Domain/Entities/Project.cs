namespace TaskSphere.Domain.Entities;

public class Project : BaseEntity<int>
{
    public string Name { get; set; } = "";
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public ICollection<Member> Members { get; set; } = new List<Member>();
    public ICollection<Sprint> Sprints { get; set; } = new List<Sprint>();
    public ICollection<Task> Tasks { get; set; } = new List<Task>();
}