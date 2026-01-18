namespace TaskSphere.Domain.DataTransferObjects.Sprint;

public class SprintDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public int? ProjectId { get; set; }
    public bool IsArchived { get; set; } 
}