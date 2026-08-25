using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Project;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class ProjectService : IProjectService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _userManager;
    private readonly IAccessControlService _accessControl;

    public ProjectService(
        IUnitOfWork unitOfWork,
        UserManager<AppUser> userManager,
        IAccessControlService accessControl)
    {
        _unitOfWork = unitOfWork;
        _userManager = userManager;
        _accessControl = accessControl;
    }

    public async Task<Result<ProjectDto>> CreateAsync(Guid companyId, CreateProjectDto dto, CancellationToken ct = default)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<ProjectDto>.Failure("Project name is required.");

        var key = (dto.Key ?? "").Trim().ToUpperInvariant();
        if (!TaskKey.IsValidProjectKey(key))
            return Result<ProjectDto>.Failure(
                "Project key must be 2-10 characters, start with a letter, and contain only letters and digits.");

        var exists = await _unitOfWork.Projects.GetCompanyProjects(companyId).AnyAsync(p => p.Name == name, ct);
        if (exists)
            return Result<ProjectDto>.Failure("Project with same name already exists.");
        
        var keyTaken = await _unitOfWork.Projects.GetCompanyProjects(companyId)
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Key == key, ct);
        if (keyTaken)
            return Result<ProjectDto>.Failure("Project key already in use.");

        var project = new Project { Name = name, Key = key, CompanyId = companyId };

        await _unitOfWork.Projects.AddAsync(project, ct);

        try
        {
            var saved = await _unitOfWork.SaveChangesAsync(ct) > 0;
            if (!saved)
                return Result<ProjectDto>.Failure("Project creation failed.");
        }
        // The pre-check above can lose a race; the unique index is the real guard.
        // Matched by index name rather than SQL error number 2601/2627, because
        // SqlException lives in Microsoft.Data.SqlClient, which this layer must not
        // reference. Any other DbUpdateException propagates untouched.
        catch (DbUpdateException ex) when (
            ex.GetBaseException().Message.Contains("IX_Projects_CompanyId_Key", StringComparison.Ordinal))
        {
            return Result<ProjectDto>.Failure("Project key already in use.");
        }

        return Result<ProjectDto>.Success(new ProjectDto(project.Id, project.Name, project.Key, project.AutoDoneOnMerge));
    }


    public async Task<Result<ProjectDto>> UpdateSettingsAsync(
        Guid companyId, int projectId, UpdateProjectSettingsDto dto, CancellationToken ct = default)
    {
        // Company-scoped through the same query every read uses, so a project in another
        // company is simply not found rather than being found and refused.
        var project = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return Result<ProjectDto>.Failure("Project not found.");

        project.AutoDoneOnMerge = dto.AutoDoneOnMerge;
        await _unitOfWork.Projects.Update(project, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProjectDto>.Success(
            new ProjectDto(project.Id, project.Name, project.Key, project.AutoDoneOnMerge));
    }
    public async Task<Result<IEnumerable<ProjectDto>>> GetAllAsync(Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct = default)
    {
        if (!isCompanyAdmin)
            return Result<IEnumerable<ProjectDto>>.Success(await _accessControl.GetAccessibleProjectsAsync(companyId, userId, ct));

        var list = await _unitOfWork.Projects.GetCompanyProjects(companyId)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Key, p.AutoDoneOnMerge))
            .ToListAsync(ct);

        return Result<IEnumerable<ProjectDto>>.Success(list);
    }

    public async Task<Result<ProjectDto>> GetByIdAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, CancellationToken ct = default)
    {
        if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, ct))
            return Result<ProjectDto>.Failure(EntityError.Forbidden);

        var project = await _unitOfWork.Projects.GetCompanyProjects(companyId)
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Key, p.AutoDoneOnMerge))
            .FirstOrDefaultAsync(ct);

        if (project == null)
            return Result<ProjectDto>.Failure("Project not found.");

        return Result<ProjectDto>.Success(project);
    }

    public async Task<Result<IEnumerable<ProjectDto>>> GetMembersProjects(Guid companyId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Result<IEnumerable<ProjectDto>>.Failure("Invalid user.");

        return Result<IEnumerable<ProjectDto>>.Success(
            await _accessControl.GetAccessibleProjectsAsync(companyId, userId, ct));
    }

    public async Task<Result<IEnumerable<MemberDto>>> GetMembersAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, CancellationToken ct = default)
    {
        if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, ct))
            return Result<IEnumerable<MemberDto>>.Failure(EntityError.Forbidden);

        var project = await _unitOfWork.Projects.GetCompanyProjectAsync(companyId, projectId, ct);
        if (project == null)
            return Result<IEnumerable<MemberDto>>.Failure("Project not found.");

        var result = project.Members
            .Where(m => m.User != null)
            .Select(m => new MemberDto(
                m.Id,
                m.ProjectId,
                m.UserId,
                m.User!.Name,
                m.User!.Email ?? ""))
            .OrderBy(x => x.UserName)
            .AsEnumerable();

        return Result<IEnumerable<MemberDto>>.Success(result);
    }

    public async Task<Result<string>> AddMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default)
    {
        if (!await _unitOfWork.Projects.CompanyOwnsProjectAsync(companyId, projectId, ct))
            return Result<string>.Failure("Project not found.");

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct);
        if (user == null)
            return Result<string>.Failure("User not found in your company.");

        var existing = await _unitOfWork.Members.GetByProjectAndUserIncludingDeletedAsync(projectId, userId, ct);

        if (existing != null)
        {
            if (!existing.IsDeleted)
                return Result<string>.Failure("User is already in this project.");

            ((ISoftDeletion)existing).Undo();
            await _unitOfWork.Members.Update(existing, ct);

            var restored = await _unitOfWork.SaveChangesAsync(ct) > 0;
            return restored
                ? Result<string>.Success("Member added.")
                : Result<string>.Failure("Add member failed.");
        }

        await _unitOfWork.Members.AddAsync(new Member { ProjectId = projectId, UserId = userId }, ct);

        var saved = await _unitOfWork.SaveChangesAsync(ct) > 0;
        if (!saved)
            return Result<string>.Failure("Add member failed.");

        return Result<string>.Success("Member added.");
    }

    public async Task<Result<string>> RemoveMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default)
    {
        if (!await _unitOfWork.Projects.CompanyOwnsProjectAsync(companyId, projectId, ct))
            return Result<string>.Failure("Project not found.");

        var member = await _unitOfWork.Members.GetAll()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

        if (member == null)
            return Result<string>.Failure("User is not a member of this project.");

        member.IsDeleted = true;
        member.DeletedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.Members.Update(member, ct);

        var saved = await _unitOfWork.SaveChangesAsync(ct) > 0;
        if (!saved)
            return Result<string>.Failure("Remove member failed.");

        return Result<string>.Success("Member removed.");
    }
}
