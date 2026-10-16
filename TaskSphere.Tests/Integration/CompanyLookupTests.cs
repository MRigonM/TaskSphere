using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskSphere.Application.Mappings;
using TaskSphere.Application.Services;
using TaskSphere.Domain.DataTransferObjects.Company;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class CompanyLookupTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereCompanyLookupTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CompanyService NewService(ApplicationDbContext db)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        return new CompanyService(new UnitOfWork(db), NullLogger<CompanyService>.Instance, mapper);
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task Reads_back_a_company_by_id()
    {
        await using var db = NewContext();
        var service = NewService(db);
        var created = await service.CreateAsync(new CompanyDto { Name = "Acme" });

        var found = await service.GetByIdAsync(created.Value!.Id);

        Assert.True(found.IsSuccess);
        Assert.Equal("Acme", found.Value!.Name);
    }

    [Fact]
    public async SystemTask.Task Reports_NotFound_for_an_id_that_does_not_exist()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var found = await service.GetByIdAsync(Guid.NewGuid());

        Assert.False(found.IsSuccess);
        Assert.Contains(found.Errors, e => e.Code == "NotFound");
    }
}
