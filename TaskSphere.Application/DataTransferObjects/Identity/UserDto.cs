namespace TaskSphere.Application.DataTransferObjects.Identity;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public bool IsDeleted { get; set; }
}