using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Services;
using TaskSphere.Auditing;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Filters;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Extensions;

public static class ApplicationServices
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
        });

        services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireLowercase = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAngularApp", policy =>
            {
                policy.WithOrigins("http://localhost:4200")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddDataProtection();
        
        services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
        
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IReadOnlyUnitOfWork, UnitOfWork>();
        services.AddScoped<IAccessControlService, AccessControlService>();

        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ISprintService, SprintService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<ITaskValidationService, TaskValidationService>();
        services.AddScoped<ISprintValidationService, SprintValidationService>();
        
        //Audit 
        services.AddTransient<AuditAttribute>();
        services.AddSingleton<AuditQueue>();
        services.AddSingleton<SensitiveDataRedactor>();
        services.AddHostedService<AuditWriterService>();

        return services;
    }
}
