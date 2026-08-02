using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Mappings;
using TaskSphere.Application.Services;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

// Aliased rather than importing TaskSphere.Domain.Entities: that namespace's Task
// entity would make every bare `Task` in this file ambiguous. TaskDto is aliased too,
// because an unqualified `Domain.` here binds to TaskSphere.Tests.Domain, not
// TaskSphere.Domain.
using Company = TaskSphere.Domain.Entities.Company;
using Project = TaskSphere.Domain.Entities.Project;
using Sprint = TaskSphere.Domain.Entities.Sprint;
using TaskDto = TaskSphere.Domain.DataTransferObjects.Task.TaskDto;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Tests.Integration;

public class TaskKeyReadPathTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereReadPathTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _projectId;
    private int _sprintId;
    private int _taskId;

    private const string OutsiderUserId = "outsider-user-id";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IMapper NewMapper()
        => new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "ReadPath Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId, NextTaskNumber = 2 };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var sprint = new Sprint
        {
            Name = "Sprint 1",
            ProjectId = _projectId,
            CompanyId = _companyId,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(14),
        };
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync();
        _sprintId = sprint.Id;

        var task = new TaskEntity
        {
            Title = "Fix the audit chart",
            ProjectId = _projectId,
            CompanyId = _companyId,
            SprintId = _sprintId,
            Number = 1,
        };
        db.Tasks.Add(task);

        // Exists but is not a Member of the project.
        db.Users.Add(new AppUser
        {
            Id = OutsiderUserId,
            Name = "Outsider",
            UserName = "outsider@example.com",
            NormalizedUserName = "OUTSIDER@EXAMPLE.COM",
            Email = "outsider@example.com",
            NormalizedEmail = "OUTSIDER@EXAMPLE.COM",
            CompanyId = _companyId,
        });

        await db.SaveChangesAsync();
        _taskId = task.Id;
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    // Each of these four fails loudly if .Include(t => t.Project) is missing, because
    // TaskKeyFormatter throws when ProjectId is set but Project was not loaded.

    [Fact]
    public async Task GetByIdForCompanyAsync_PopulatesKey()
    {
        await using var db = NewContext();
        var entity = await new TaskRepository(db).GetByIdForCompanyAsync(_taskId, _companyId, CancellationToken.None);

        Assert.Equal("TS-1", NewMapper().Map<TaskDto>(entity!).Key);
    }

    [Fact]
    public async Task GetByProjectAsync_PopulatesKey()
    {
        await using var db = NewContext();
        var list = await new TaskRepository(db).GetByProjectAsync(_projectId, _companyId, CancellationToken.None);

        Assert.Equal("TS-1", NewMapper().Map<List<TaskDto>>(list).Single().Key);
    }

    [Fact]
    public async Task GetBySprintAsync_PopulatesKey()
    {
        await using var db = NewContext();
        var list = await new TaskRepository(db).GetBySprintAsync(_sprintId, _companyId, CancellationToken.None);

        Assert.Equal("TS-1", NewMapper().Map<List<TaskDto>>(list).Single().Key);
    }

    [Fact]
    public async Task GetBacklogAsync_PopulatesKey()
    {
        await using var db = NewContext();

        // Backlog means no sprint, so move the task off the sprint first.
        var task = await db.Tasks.SingleAsync(t => t.Id == _taskId);
        task.SprintId = null;
        await db.SaveChangesAsync();

        var list = await new TaskRepository(db).GetBacklogAsync(_projectId, _companyId, CancellationToken.None);

        Assert.Equal("TS-1", NewMapper().Map<List<TaskDto>>(list).Single().Key);
    }

    [Fact]
    public async Task GetByKeyAsync_ReturnsForbidden_NotNotFound_ForNonMemberAskingForAMissingNumber()
    {
        await using var db = NewContext();

        var service = new TaskService(
            new UnitOfWork(db),
            new AccessControlService(db),
            new TaskValidationService(new AccessControlService(db), new UnitOfWork(db)),
            new TaskNumberAllocator(db),
            NewMapper());

        // TS-999 does not exist AND the caller is not a member. If the access check
        // ever moves after the task lookup, this returns NotFound and the endpoint
        // becomes a probe for which task numbers exist.
        var result = await service.GetByKeyAsync("TS-999", _companyId, OutsiderUserId, false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Auth.Forbidden");
    }
}
