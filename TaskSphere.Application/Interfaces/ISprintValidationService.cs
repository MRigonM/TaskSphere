using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Sprint;

namespace TaskSphere.Application.Interfaces;

public interface ISprintValidationService
{
    Task<Result<ValidatedSprintInput>> ValidateSprintCreateAsync(Guid companyId, CreateSprintDto dto, CancellationToken ct = default);
    Task<Result<ValidatedSprintInput>> ValidateSprintUpdateAsync(UpdateSprintDto dto, CancellationToken ct = default);
    Task<Result<bool>> ValidateSprintArchiveAsync(Guid companyId, int sprintId, bool isArchived, CancellationToken ct = default);
}

public record ValidatedSprintInput(string Name, DateTime StartDate, DateTime EndDate);
