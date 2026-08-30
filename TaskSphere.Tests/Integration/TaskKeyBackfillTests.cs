using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Startup;

// Aliased rather than importing TaskSphere.Domain.Entities: that namespace's Task
// entity would make every bare `Task` in this file ambiguous.
using Company = TaskSphere.Domain.Entities.Company;
using Project = TaskSphere.Domain.Entities.Project;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Tests.Integration;

public class TaskKeyBackfillTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereBackfillTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);

    }
    /// <summary>
    /// The backfill's whole purpose is to run against data that does not yet satisfy the
    /// task-key unique indexes: seeding two tasks with the default Number = 0 would violate
    /// IX_Tasks_ProjectId_Number before the backfill ever ran. Production applied the column
    /// migration, backfilled, then applied the index migration — these tests reproduce that
    /// state by migrating to the CURRENT schema and dropping the two indexes again.
    /// <para>
    /// It used to stop the migrator at AddTaskKeyColumns instead. That pinned the database to
    /// a 2026-08 schema while the EF model stayed current, so the first column added to
    /// Projects or Tasks after that date broke every insert here with "Invalid column name".
    /// AutoDoneOnMerge was that column.
    /// </para>
    /// </summary>
    private const string DropTaskKeyIndexes =
        "DROP INDEX IX_Tasks_ProjectId_Number ON Tasks;" +
        "DROP INDEX IX_Projects_CompanyId_Key ON Projects;";

    /// <summary>
    /// The same two indexes, recreated. Applying these after the backfill is itself an
    /// assertion: a unique index only creates successfully if the backfill left every key and
    /// number valid. This is what the second half of the round-trip used to get from running
    /// the index migration, which is now already applied by <see cref="InitializeAsync"/>.
    /// Mirrors 20260801152814_AddTaskKeyUniqueIndexes.
    /// </summary>
    private const string CreateTaskKeyIndexes =
        "CREATE UNIQUE INDEX IX_Tasks_ProjectId_Number ON Tasks ([ProjectId], [Number]) " +
        "WHERE [ProjectId] IS NOT NULL;" +
        "CREATE UNIQUE INDEX IX_Projects_CompanyId_Key ON Projects ([CompanyId], [Key]);";

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(DropTaskKeyIndexes);
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(ConnectionString));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Backfill_NumbersSoftDeletedTasksToo_SoNumbersAreNeverReused()
    {
        Guid companyId;
        int projectId;

        await using (var db = NewContext())
        {
            var company = new Company { Name = "Backfill Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();

            // Key deliberately left as "" so the backfill picks this project up.
            var project = new Project { Name = "TaskSphere", CompanyId = company.Id };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            companyId = company.Id;
            projectId = project.Id;

            db.Tasks.AddRange(
                new TaskEntity { Title = "first",  ProjectId = projectId, CompanyId = companyId },
                new TaskEntity { Title = "second", ProjectId = projectId, CompanyId = companyId, IsDeleted = true },
                new TaskEntity { Title = "third",  ProjectId = projectId, CompanyId = companyId });

            await db.SaveChangesAsync();
        }

        await using var provider = BuildProvider();
        var backfill = new TaskKeyBackfillService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TaskKeyBackfillService>.Instance);

        await backfill.StartAsync(CancellationToken.None);

        await using (var db = NewContext())
        {
            // Applying migration 2 now is itself an assertion: the unique indexes only
            // create successfully if the backfill left every key and number valid.
            await db.Database.ExecuteSqlRawAsync(CreateTaskKeyIndexes);

            var project = await db.Projects.SingleAsync(p => p.Id == projectId);
            Assert.Equal("TS", project.Key);
            Assert.Equal(4, project.NextTaskNumber);

            var tasks = await db.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.ProjectId == projectId)
                .OrderBy(t => t.Id)
                .ToListAsync();

            // The soft-deleted task must hold number 2 — if it were skipped, a future
            // task would reuse 2 and every old reference to it would resolve wrongly.
            Assert.Equal([1, 2, 3], tasks.Select(t => t.Number));
            Assert.Equal(3, tasks.Select(t => t.Number).Distinct().Count());
        }
    }

    [Fact]
    public async Task Backfill_IsIdempotent()
    {
        await using (var db = NewContext())
        {
            var company = new Company { Name = "Idempotent Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();

            db.Projects.Add(new Project { Name = "StayEase", CompanyId = company.Id });
            await db.SaveChangesAsync();
        }

        await using var provider = BuildProvider();
        var backfill = new TaskKeyBackfillService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TaskKeyBackfillService>.Instance);

        await backfill.StartAsync(CancellationToken.None);
        await backfill.StartAsync(CancellationToken.None);

        await using (var db = NewContext())
        {
            var project = await db.Projects.SingleAsync();
            Assert.Equal("SE", project.Key);
            Assert.Equal(1, project.NextTaskNumber);
        }
    }
}
