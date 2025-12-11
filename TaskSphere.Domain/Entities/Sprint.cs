namespace TaskSphere.Domain.Entities;

public class Sprint : BaseEntity<int>
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
}