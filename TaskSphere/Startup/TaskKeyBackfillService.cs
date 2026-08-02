using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Startup;

/// <summary>
/// One-off, idempotent backfill: assigns a Key to every project that has none and
/// numbers that project's tasks in Id order. No-ops once every project has a key,
/// so it is safe to leave registered permanently.
/// </summary>
public sealed class TaskKeyBackfillService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskKeyBackfillService> _logger;

    public TaskKeyBackfillService(IServiceScopeFactory scopeFactory, ILogger<TaskKeyBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var unkeyed = await db.Projects
            .IgnoreQueryFilters()
            .Where(p => p.Key == "")
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        if (unkeyed.Count == 0)
            return;

        var existing = await db.Projects
            .IgnoreQueryFilters()
            .Where(p => p.Key != "")
            .Select(p => new { p.CompanyId, p.Key })
            .ToListAsync(cancellationToken);

        var taken = existing
            .GroupBy(x => x.CompanyId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToHashSet(StringComparer.Ordinal));

        foreach (var project in unkeyed)
        {
            if (!taken.TryGetValue(project.CompanyId, out var companyKeys))
            {
                companyKeys = new HashSet<string>(StringComparer.Ordinal);
                taken[project.CompanyId] = companyKeys;
            }

            project.Key = ProjectKeyGenerator.Derive(project.Name, companyKeys);
            companyKeys.Add(project.Key);
            
            var tasks = await db.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.ProjectId == project.Id)
                .OrderBy(t => t.Id)
                .ToListAsync(cancellationToken);

            var next = 1;
            foreach (var task in tasks)
                task.Number = next++;

            project.NextTaskNumber = next;

            _logger.LogInformation(
                "Backfilled project {ProjectId} with key {Key} and {TaskCount} tasks.",
                project.Id, project.Key, tasks.Count);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}