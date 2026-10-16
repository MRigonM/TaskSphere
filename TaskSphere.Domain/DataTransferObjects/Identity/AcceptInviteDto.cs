namespace TaskSphere.Domain.DataTransferObjects.Identity;

/// <summary>
/// Posted by the accept-invite screen. Carries the same four fields as
/// <see cref="ResetPasswordDto"/> and stays a separate type because the two flows are separate
/// endpoints with different wording and different client screens.
/// </summary>
public class AcceptInviteDto
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}
