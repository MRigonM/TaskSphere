using Microsoft.AspNetCore.Identity;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Domain.Entities.Identity;

public class AppUser : IdentityUser, ISoftDeletion
{
    public string Name { get; set; }
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}