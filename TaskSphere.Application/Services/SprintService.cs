using AutoMapper;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class SprintService : ISprintService
{
    private readonly ISprintRepository _sprintRepository;
    private readonly IAccessControlService _accessControl;
    private readonly ISprintValidationService _validationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public SprintService(
        ISprintRepository sprintRepository,
        IAccessControlService accessControl,
        ISprintValidationService validationService,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _sprintRepository = sprintRepository;
        _accessControl = accessControl;
        _validationService = validationService;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<List<SprintDto>>> GetByProjectAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, bool includeArchived, CancellationToken ct)
    {
        if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, ct))
            return Result<List<SprintDto>>.Failure(EntityError.Forbidden);

        var sprints = await _sprintRepository.GetByProjectAsync(projectId, companyId, includeArchived, ct);
        return Result<List<SprintDto>>.Success(_mapper.Map<List<SprintDto>>(sprints));
    }

    public async Task<Result<SprintDto>> GetByIdAsync(Guid companyId, int sprintId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        if (!isCompanyAdmin && !await _accessControl.CanAccessSprintAsync(companyId, userId, sprintId, ct))
            return Result<SprintDto>.Failure(EntityError.Forbidden);

        var sprint = await _sprintRepository.GetWithProjectAsync(sprintId, companyId, ct);
        if (sprint == null)
            return Result<SprintDto>.Failure("Sprint not found.");

        return Result<SprintDto>.Success(_mapper.Map<SprintDto>(sprint));
    }

    public async Task<Result<SprintDto>> CreateAsync(Guid companyId, CreateSprintDto dto, CancellationToken ct)
    {
        var validation = await _validationService.ValidateSprintCreateAsync(companyId, dto, ct);
        if (!validation.IsSuccess || validation.Value is null)
            return Result<SprintDto>.Failure(validation.Errors.ToArray());

        var sprint = new Domain.Entities.Sprint
        {
            Name = validation.Value.Name,
            StartDate = validation.Value.StartDate,
            EndDate = validation.Value.EndDate,
            IsActive = dto.IsActive,
            ProjectId = dto.ProjectId,
            CompanyId = companyId
        };

        if (dto.IsActive)
        {
            sprint = await _sprintRepository.CreateAndActivateAsync(
                sprint,
                companyId,
                deactivateOtherSprintsInProject: true,
                ct
            );
        }
        else
        {
            await _sprintRepository.AddAsync(sprint, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result<SprintDto>.Success(_mapper.Map<SprintDto>(sprint));
    }

    public async Task<Result<SprintDto>> UpdateAsync(Guid companyId, int sprintId, UpdateSprintDto dto, CancellationToken ct)
    {
        var sprint = await _sprintRepository.GetByIdAsync(sprintId, ct);
        if (sprint == null || sprint.CompanyId != companyId)
            return Result<SprintDto>.Failure("Sprint not found.");

        var validation = await _validationService.ValidateSprintUpdateAsync(dto, ct);
        if (!validation.IsSuccess || validation.Value is null)
            return Result<SprintDto>.Failure(validation.Errors.ToArray());

        sprint.Name = validation.Value.Name;
        sprint.StartDate = validation.Value.StartDate;
        sprint.EndDate = validation.Value.EndDate;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SprintDto>.Success(_mapper.Map<SprintDto>(sprint));
    }

    public async Task<Result<bool>> SetActiveAsync(Guid companyId, int sprintId, bool isActive, CancellationToken ct)
    {
        await _sprintRepository.SetActiveAsync(sprintId, companyId, isActive, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ActivateExistingAndCarryOverAsync(Guid companyId, int sprintId,
        bool carryOverUnfinished, CancellationToken ct)
    {
        await _sprintRepository.ActivateExistingAndCarryOverAsync(sprintId, companyId, carryOverUnfinished, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<SprintBoardDto>> GetBoardAsync(Guid companyId, int sprintId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        if (!isCompanyAdmin && !await _accessControl.CanAccessSprintAsync(companyId, userId, sprintId, ct))
            return Result<SprintBoardDto>.Failure(EntityError.Forbidden);

        var board = await _sprintRepository.GetBoardAsync(sprintId, companyId, ct);
        if (board == null)
            return Result<SprintBoardDto>.Failure("Sprint not found.");

        return Result<SprintBoardDto>.Success(board);
    }

    public async Task<Result<bool>> MoveTaskToActiveAsync(Guid companyId, int sprintId, int taskId,
        CancellationToken ct)
    {
        await _sprintRepository.MoveTaskToActiveAsync(taskId, sprintId, companyId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SetArchivedAsync(Guid companyId, int sprintId, bool isArchived, CancellationToken ct)
    {
        var validation = await _validationService.ValidateSprintArchiveAsync(companyId, sprintId, isArchived, ct);
        if (!validation.IsSuccess)
            return Result<bool>.Failure(validation.Errors.ToArray());

        var sprint = await _sprintRepository.GetByCompanyAsync(companyId, sprintId, ct);
        if (sprint == null)
            return Result<bool>.Failure("Sprint not found.");

        sprint.IsArchived = isArchived;

        await _sprintRepository.Update(sprint, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
