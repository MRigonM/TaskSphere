using Microsoft.AspNetCore.Identity;

namespace TaskSphere.Domain.Entities.Identity;

public class AppUser : IdentityUser
{
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
}