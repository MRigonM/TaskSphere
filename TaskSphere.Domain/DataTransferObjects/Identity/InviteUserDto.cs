namespace TaskSphere.Domain.DataTransferObjects.Identity;

/// <summary>
/// What an admin supplies to add a member. No password fields: the member sets their own through
/// the emailed link, which is also what confirms their address.
/// </summary>
public class InviteUserDto
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
