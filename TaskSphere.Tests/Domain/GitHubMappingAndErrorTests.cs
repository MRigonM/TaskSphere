using AutoMapper;
using TaskSphere.Application.Mappings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;

namespace TaskSphere.Tests.Domain;

public class GitHubMappingAndErrorTests
{
    private static IMapper NewMapper()
        => new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    // NOTE: a whole-profile AssertConfigurationIsValid() cannot be asserted here. Five
    // pre-existing DTO -> entity maps (CompanyDto, CreateSprintDto, UpdateSprintDto,
    // CreateTaskDto, UpdateTaskDto) leave IsDeleted / DeletedAt / UpdatedAtUtc unmapped, so
    // the profile has never passed that check. Fixing them is outside sub-project B1's scope.
    // The GitHub maps are verified individually below instead.

    [Fact]
    public void InstallationDto_ExposesNoSoftDeleteOrTenantFields()
    {
        // Entities carry CompanyId, IsDeleted and DeletedAt; none of them belong on the wire.
        var names = typeof(GitHubInstallationDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("IsDeleted", names);
        Assert.DoesNotContain("DeletedAt", names);
        Assert.DoesNotContain("CompanyId", names);
    }

    [Fact]
    public void RepositoryDto_ExposesNoSoftDeleteOrTenantFields()
    {
        var names = typeof(GitHubRepositoryDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("IsDeleted", names);
        Assert.DoesNotContain("DeletedAt", names);
        Assert.DoesNotContain("CompanyId", names);
    }

    [Fact]
    public void LinkDto_ExposesNoSoftDeleteOrTenantFields()
    {
        var names = typeof(ProjectRepositoryLinkDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("IsDeleted", names);
        Assert.DoesNotContain("DeletedAt", names);
        Assert.DoesNotContain("CompanyId", names);
    }

    [Fact]
    public void InstallationMapsToDto()
    {
        var installation = new GitHubInstallation
        {
            Id = 3,
            InstallationId = 12345,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
            IsSuspended = true,
        };

        var dto = NewMapper().Map<GitHubInstallationDto>(installation);

        Assert.Equal(3, dto.Id);
        Assert.Equal(12345, dto.InstallationId);
        Assert.Equal("rigon-org", dto.AccountLogin);
        Assert.Equal(RepositorySelection.All, dto.RepositorySelection);
        Assert.True(dto.IsSuspended);
    }

    [Fact]
    public void LinkMapsToDto_FlatteningRepositoryFullName()
    {
        var link = new ProjectRepositoryLink
        {
            Id = 9,
            ProjectId = 4,
            GitHubRepositoryId = 7,
            LinkedByUserId = "user-1",
            Repository = new GitHubRepository { Id = 7, FullName = "rigon-org/tasksphere" },
        };

        var dto = NewMapper().Map<ProjectRepositoryLinkDto>(link);

        Assert.Equal("rigon-org/tasksphere", dto.FullName);
    }

    [Fact]
    public void LinkMapsToDto_WithoutRepositoryLoaded_YieldsEmptyFullNameRatherThanThrowing()
    {
        // Unlike TaskKeyFormatter, a missing Include here is not a programming error worth
        // throwing over: Task 17 must still render links whose repository was soft-deleted
        // by a disconnect.
        var link = new ProjectRepositoryLink { Id = 9, ProjectId = 4, GitHubRepositoryId = 7, LinkedByUserId = "user-1" };

        var dto = NewMapper().Map<ProjectRepositoryLinkDto>(link);

        Assert.Equal("", dto.FullName);
    }

    [Fact]
    public void EntityErrorConflict_UsesTheCodeApiBaseControllerMapsTo409()
    {
        var error = EntityError.Conflict("Already connected to another company.");

        Assert.Equal("Conflict", error.Code);
        Assert.Equal("Already connected to another company.", error.Description);
    }
}
