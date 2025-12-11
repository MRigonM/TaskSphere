namespace TaskSphere.Domain.Entities;

public class Task : BaseEntity<int>
{
    public string? SprintId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "ToDo";
    public string? Priority { get; set; }
    public int? AssigneeUserId { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int? StoryPoints { get; set; }
}