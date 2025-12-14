namespace TaskSphere.Application.DataTransferObjects.Company;

public class CreateMemberDto
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmPassword { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = new();
}