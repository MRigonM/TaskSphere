namespace TaskSphere.Domain.DataTransferObjects.Identity;

public class UserQueryDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
