using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Sprint;

namespace TaskSphere.Application.Interfaces;

public interface ISprintService
{
    Task<Result<List<SprintDto>>> GetByProjectAsync(Guid companyId, int projectId, CancellationToken ct);
    Task<Result<SprintDto>> GetByIdAsync(Guid companyId, int sprintId, CancellationToken ct);
    Task<Result<SprintDto>> CreateAsync(Guid companyId, CreateSprintDto dto, CancellationToken ct);
    Task<Result<SprintDto>> UpdateAsync(Guid companyId, int sprintId, UpdateSprintDto dto, CancellationToken ct);
    Task<Result<bool>> SetActiveAsync(Guid companyId, int sprintId, bool isActive, CancellationToken ct);
    Task<Result<bool>> ActivateExistingAndCarryOverAsync(Guid companyId, int sprintId, bool carryOverUnfinished, CancellationToken ct);
    Task<Result<SprintBoardDto>> GetBoardAsync(Guid companyId, int sprintId, CancellationToken ct);
    Task<Result<bool>> MoveTaskToActiveAsync(Guid companyId, int sprintId, int taskId, CancellationToken ct);
}