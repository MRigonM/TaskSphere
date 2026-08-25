using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskSphere.Infrastructure.Configuration;
using TaskSphere.Infrastructure.Services;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Mappings;
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
        
        // The assembly holding the profiles, not every assembly loaded in the process:
        // GetTypes() throws ReflectionTypeLoadException on any assembly whose types cannot be
        // enumerated, which takes startup down with it. MappingProfile is the only Profile in
        // the solution, so this is equivalent and cannot fail on an unrelated dependency.
        services.AddAutoMapper(typeof(MappingProfile).Assembly);
        
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IReadOnlyUnitOfWork, UnitOfWork>();
        services.AddScoped<IAccessControlService, AccessControlService>();
        services.AddScoped<ITaskNumberAllocator, TaskNumberAllocator>();

        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ISprintService, SprintService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ITaskValidationService, TaskValidationService>();
        services.AddScoped<ISprintValidationService, SprintValidationService>();
        
        //Audit 
        services.AddTransient<AuditAttribute>();
        services.AddSingleton<AuditQueue>();
        services.AddSingleton<SensitiveDataRedactor>();
        services.AddHostedService<AuditWriterService>();

        services.AddHostedService<TaskSphere.Startup.TaskKeyBackfillService>();

        //GitHub
        services.AddMemoryCache();

        services.AddOptions<GitHubAppOptions>()
            .Bind(configuration.GetSection(GitHubAppOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GitHubAppOptions>, GitHubAppOptionsValidator>();

        // Singleton: it owns an RSA key that must outlive any single request, because
        // Microsoft.IdentityModel caches signature providers against it.
        services.AddSingleton<IGitHubAppJwtProvider, GitHubAppJwtProvider>();

        services.AddScoped<IGitHubInstallStateService, GitHubInstallStateService>();
        services.AddScoped<IGitHubConnectionService, GitHubConnectionService>();
        services.AddScoped<IGitHubRepositorySyncService, GitHubRepositorySyncService>();
        services.AddScoped<IGitHubConnectionReadService, GitHubConnectionReadService>();
        services.AddScoped<IGitHubProjectLinkService, GitHubProjectLinkService>();
        services.AddScoped<IGitHubTaskLinkResolver, GitHubTaskLinkResolver>();
        services.AddScoped<IGitHubActivitySyncService, GitHubActivitySyncService>();
        services.AddScoped<IMergeTransitionService, MergeTransitionService>();
        services.AddScoped<GitHubPullRequestMirror>();
        services.AddScoped<IGitHubTaskActivityService, GitHubTaskActivityService>();
        services.AddScoped<IGitHubBranchService, GitHubBranchService>();

        services.AddHttpClient<IGitHubTokenService, GitHubTokenService>(ConfigureGitHubApiClient);
        services.AddHttpClient<IGitHubApiClient, GitHubApiClient>(ConfigureGitHubApiClient);

        // Deliberately NOT ConfigureGitHubApiClient (§0p): the OAuth token exchange needs
        // Accept: application/json, and this client must never share a handler chain with the
        // installation-token clients — a user token crossing into that path is a tenancy leak.
        services.AddHttpClient<IGitHubUserAuthService, GitHubUserAuthService>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskSphere", "1.0"));
        });

        return services;
    }

    // GitHub rejects requests without a User-Agent, and pins response shape to the API
    // version header. Applied to both GitHub clients.
    private static void ConfigureGitHubApiClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskSphere", "1.0"));
    }
}
