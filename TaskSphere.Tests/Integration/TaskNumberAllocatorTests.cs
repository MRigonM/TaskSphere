using Microsoft.EntityFrameworkCore;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Services;

// Aliased rather than importing the namespace: TaskSphere.Domain.Entities.Task
// would make every bare `Task` in this file ambiguous.
using Company = TaskSphere.Domain.Entities.Company;
using Project = TaskSphere.Domain.Entities.Project;

namespace TaskSphere.Tests.Integration;

public class TaskNumberAllocatorTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAllocatorTests;Trusted_Connection=True;TrustServerCertificate=True";

    private int _projectId;
    private Guid _companyId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Allocator Test Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var project = new Project { Name = "Allocator Test", Key = "AT", CompanyId = company.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _projectId = project.Id;
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task AllocateAsync_ReturnsSequentialNumbers()
    {
        await using var db = NewContext();
        var allocator = new TaskNumberAllocator(db);

        Assert.Equal(1, await allocator.AllocateAsync(_projectId, CancellationToken.None));
        Assert.Equal(2, await allocator.AllocateAsync(_projectId, CancellationToken.None));
        Assert.Equal(3, await allocator.AllocateAsync(_projectId, CancellationToken.None));
    }

    [Fact]
    public async Task AllocateAsync_ReturnsNull_WhenProjectDoesNotExist()
    {
        await using var db = NewContext();
        var allocator = new TaskNumberAllocator(db);

        Assert.Null(await allocator.AllocateAsync(999_999, CancellationToken.None));
    }

    [Fact]
    public async Task AllocateAsync_NeverIssuesTheSameNumberTwice_UnderConcurrency()
    {
        const int concurrentCalls = 50;

        // Each call gets its own DbContext — DbContext is not thread-safe.
        var allocations = await Task.WhenAll(
            Enumerable.Range(0, concurrentCalls).Select(async _ =>
            {
                await using var db = NewContext();
                var allocator = new TaskNumberAllocator(db);
                return await allocator.AllocateAsync(_projectId, CancellationToken.None);
            }));

        var numbers = allocations.Select(n => n!.Value).ToList();

        Assert.Equal(concurrentCalls, numbers.Count);
        Assert.Equal(concurrentCalls, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, concurrentCalls), numbers.OrderBy(n => n));
    }
}
