using AutoMapper;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class SprintService : ISprintService
{
    private readonly ISprintRepository _sprintRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public SprintService(ISprintRepository sprintRepository, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _sprintRepository = sprintRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<List<SprintDto>>> GetByProjectAsync(Guid companyId, int projectId, bool includeArchived, CancellationToken ct)
    {
        var sprints = await _sprintRepository.GetByProjectAsync(projectId, companyId, includeArchived, ct);
        return Result<List<SprintDto>>.Success(_mapper.Map<List<SprintDto>>(sprints));
    }

    public async Task<Result<SprintDto>> GetByIdAsync(Guid companyId, int sprintId, CancellationToken ct)
    {
        var sprint = await _sprintRepository.GetWithProjectAsync(sprintId, companyId, ct);
        if (sprint == null)
            return Result<SprintDto>.Failure("Sprint not found.");

        return Result<SprintDto>.Success(_mapper.Map<SprintDto>(sprint));
    }

    public async Task<Result<SprintDto>> CreateAsync(Guid companyId, CreateSprintDto dto, CancellationToken ct)
    {
        var sprint = _mapper.Map<Sprint>(dto);
        sprint.Name = sprint.Name.Trim();
        sprint.CompanyId = companyId;

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

        _mapper.Map(dto, sprint);
        sprint.Name = sprint.Name.Trim();

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

    public async Task<Result<SprintBoardDto>> GetBoardAsync(Guid companyId, int sprintId, CancellationToken ct)
    {
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
        var sprint = await _sprintRepository.GetByCompanyAsync(companyId, sprintId, ct);
        if (sprint == null) return Result<bool>.Failure("Sprint not found.");

        if (isArchived && sprint.IsActive)
            return Result<bool>.Failure("Active sprint cannot be archived. Set it inactive first.");

        sprint.IsArchived = isArchived;

        await _sprintRepository.Update(sprint, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}