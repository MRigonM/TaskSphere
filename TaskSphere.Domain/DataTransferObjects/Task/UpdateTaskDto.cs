namespace TaskSphere.Domain.DataTransferObjects.Task;

public class UpdateTaskDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";
    public string? Priority { get; set; }
    public int? StoryPoints { get; set; }
    public string? AssigneeUserId { get; set; }
    public int? SprintId { get; set; }
}
