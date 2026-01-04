namespace TaskSphere.Domain.DataTransferObjects.Identity;

public class UpdateUserDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string? NewPassword { get; set; }
    public string? ConfirmNewPassword { get; set; }
}
