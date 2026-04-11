using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class SprintValidationService : ISprintValidationService
{
    private readonly IProjectRepository _projectRepository;
    private readonly ISprintRepository _sprintRepository;

    public SprintValidationService(IProjectRepository projectRepository, ISprintRepository sprintRepository)
    {
        _projectRepository = projectRepository;
        _sprintRepository = sprintRepository;
    }

    public async Task<Result<ValidatedSprintInput>> ValidateSprintCreateAsync(Guid companyId, CreateSprintDto dto, CancellationToken ct = default)
    {
        if (!await _projectRepository.CompanyOwnsProjectAsync(companyId, dto.ProjectId, ct))
            return Result<ValidatedSprintInput>.Failure(new Error("Validation.InvalidProject", "Project not found."));

        var name = NormalizeName(dto.Name);
        if (name is null)
            return Result<ValidatedSprintInput>.Failure(new Error("Validation.InvalidSprintName", "Sprint name is required."));

        if (dto.StartDate > dto.EndDate)
            return Result<ValidatedSprintInput>.Failure(new Error("Validation.InvalidSprintDates", "Sprint start date must be on or before the end date."));

        return Result<ValidatedSprintInput>.Success(new ValidatedSprintInput(name, dto.StartDate, dto.EndDate));
    }

    public Task<Result<ValidatedSprintInput>> ValidateSprintUpdateAsync(UpdateSprintDto dto, CancellationToken ct = default)
    {
        var name = NormalizeName(dto.Name);
        if (name is null)
            return Task.FromResult(Result<ValidatedSprintInput>.Failure(new Error("Validation.InvalidSprintName", "Sprint name is required.")));

        if (dto.StartDate > dto.EndDate)
            return Task.FromResult(Result<ValidatedSprintInput>.Failure(new Error("Validation.InvalidSprintDates", "Sprint start date must be on or before the end date.")));

        return Task.FromResult(Result<ValidatedSprintInput>.Success(new ValidatedSprintInput(name, dto.StartDate, dto.EndDate)));
    }

    public async Task<Result<bool>> ValidateSprintArchiveAsync(Guid companyId, int sprintId, bool isArchived, CancellationToken ct = default)
    {
        var sprint = await _sprintRepository.GetByCompanyAsync(companyId, sprintId, ct);
        if (sprint is null)
            return Result<bool>.Failure("Sprint not found.");

        if (isArchived && sprint.IsActive)
            return Result<bool>.Failure("Active sprint cannot be archived. Set it inactive first.");

        return Result<bool>.Success(true);
    }

    private static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return name.Trim();
    }
}
