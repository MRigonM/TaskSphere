using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace TaskSphere.Domain.Interfaces;

public interface ISprintRepository : IGenericRepository<Sprint, int>
{
    Task<List<Sprint>> GetByProjectAsync(int projectId, Guid companyId, CancellationToken ct);
    Task<Sprint?> GetWithProjectAsync(int sprintId, Guid companyId, CancellationToken ct);
    Task<Sprint?> GetActiveForProjectAsync(int projectId, Guid companyId, CancellationToken ct);
    Task SetActiveAsync(int sprintId, Guid companyId, bool isActive, CancellationToken ct);
    Task<Sprint> CreateAndActivateAsync(Sprint sprint, Guid companyId, bool deactivateOtherSprintsInProject, CancellationToken ct);
    Task ActivateExistingAndCarryOverAsync(int sprintId, Guid companyId, bool carryOverUnfinished, CancellationToken ct);
    Task<SprintBoardDto?> GetBoardAsync(int sprintId, Guid companyId, CancellationToken ct);
    Task MoveTaskToActiveAsync(int taskId, int sprintId, Guid companyId, CancellationToken ct);
}