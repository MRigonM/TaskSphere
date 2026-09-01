namespace TaskSphere.Domain.DataTransferObjects.Identity;

/// <summary>Shared by resend-verification and (in Plan B) forgot-password.</summary>
public class EmailOnlyDto
{
    public string Email { get; set; } = string.Empty;
}
