namespace TaskSphere.Domain.Entities;

public class Task : BaseEntity<int>
{
    public string? SprintId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "ToDo";
    public string? Priority { get; set; }
    public string? AssigneeUserId { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public int? StoryPoints { get; set; }
}