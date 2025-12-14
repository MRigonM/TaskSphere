using Microsoft.AspNetCore.Identity;
using TaskSphere.Domain.Enums;

namespace TaskSphere.Extensions;

public static class RoleSeedingExtensions
{
    public static async Task SeedRolesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in new[] { Roles.Company, Roles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}