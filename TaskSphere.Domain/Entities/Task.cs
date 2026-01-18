namespace TaskSphere.Domain.Entities;

public class Task : BaseEntity<int>
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";
    public string? Priority { get; set; }
    public string? AssigneeUserId { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? StoryPoints { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public int? SprintId { get; set; }
    public Sprint? Sprint { get; set; }
}