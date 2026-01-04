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
    private readonly IProjectRepository _projects;
    private readonly IMemberRepository _memberRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _userManager;

    public ProjectService(
        IProjectRepository projects,
        IMemberRepository memberRepository,
        IUnitOfWork unitOfWork,
        UserManager<AppUser> userManager)
    {
        _projects = projects;
        _memberRepository = memberRepository;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    public async Task<Result<ProjectDto>> CreateAsync(Guid companyId, CreateProjectDto dto, CancellationToken ct = default)
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<ProjectDto>.Failure("Project name is required.");

        var exists = await _projects.GetCompanyProjects(companyId).AnyAsync(p => p.Name == name, ct);
        if (exists)
            return Result<ProjectDto>.Failure("Project with same name already exists.");

        var project = new Project { Name = name, CompanyId = companyId };

        await _projects.AddAsync(project, ct);
        var saved = await _unitOfWork.SaveChangesAsync(ct) > 0;
        if (!saved)
            return Result<ProjectDto>.Failure("Project creation failed.");

        return Result<ProjectDto>.Success(new ProjectDto(project.Id, project.Name));
    }

    public async Task<Result<IEnumerable<ProjectDto>>> GetAllAsync(Guid companyId, CancellationToken ct = default)
    {
        var list = await _projects.GetCompanyProjects(companyId)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.Name))
            .ToListAsync(ct);

        return Result<IEnumerable<ProjectDto>>.Success(list);
    }

    public async Task<Result<IEnumerable<MemberDto>>> GetMembersAsync(Guid companyId, int projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetCompanyProjectAsync(companyId, projectId, ct);
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
        if (!await _projects.CompanyOwnsProjectAsync(companyId, projectId, ct))
            return Result<string>.Failure("Project not found.");

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct);
        if (user == null)
            return Result<string>.Failure("User not found in your company.");

        var existing = await _memberRepository.GetByProjectAndUserIncludingDeletedAsync(projectId, userId, ct);

        if (existing != null)
        {
            if (!existing.IsDeleted)
                return Result<string>.Failure("User is already in this project.");

            ((ISoftDeletion)existing).Undo();
            await _memberRepository.Update(existing, ct);

            var restored = await _unitOfWork.SaveChangesAsync(ct) > 0;
            return restored
                ? Result<string>.Success("Member added.")
                : Result<string>.Failure("Add member failed.");
        }

        await _memberRepository.AddAsync(new Member { ProjectId = projectId, UserId = userId }, ct);

        var saved = await _unitOfWork.SaveChangesAsync(ct) > 0;
        if (!saved)
            return Result<string>.Failure("Add member failed.");

        return Result<string>.Success("Member added.");
    }

    public async Task<Result<string>> RemoveMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default)
    {
        if (!await _projects.CompanyOwnsProjectAsync(companyId, projectId, ct))
            return Result<string>.Failure("Project not found.");

        var member = await _memberRepository.GetAll()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

        if (member == null)
            return Result<string>.Failure("User is not a member of this project.");

        member.IsDeleted = true;
        member.DeletedAt = DateTimeOffset.UtcNow;

        await _memberRepository.Update(member, ct);

        var saved = await _unitOfWork.SaveChangesAsync(ct) > 0;
        if (!saved)
            return Result<string>.Failure("Remove member failed.");

        return Result<string>.Success("Member removed.");
    }
}