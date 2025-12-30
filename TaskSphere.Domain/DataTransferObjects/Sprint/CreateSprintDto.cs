namespace TaskSphere.Domain.DataTransferObjects.Sprint;

public class CreateSprintDto
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public int ProjectId { get; set; }
}