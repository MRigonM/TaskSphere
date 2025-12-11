using Microsoft.AspNetCore.Identity;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Domain.Entities.Identity;

public class AppUser : IdentityUser, ISoftDeletion
{
    public string FirstName { get; set; }

    public string LastName { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}