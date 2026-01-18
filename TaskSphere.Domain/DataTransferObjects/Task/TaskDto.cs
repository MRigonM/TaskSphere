namespace TaskSphere.Domain.DataTransferObjects.Task;

public class TaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";
    public string? Priority { get; set; }
    public int? StoryPoints { get; set; }
    public int? ProjectId { get; set; }
    public int? SprintId { get; set; }
    public string? AssigneeUserId { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}