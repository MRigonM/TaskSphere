using AutoMapper;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Interfaces;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Application.Services;

public class TaskService : ITaskService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;
    private readonly ITaskValidationService _validationService;
    private readonly IMapper _mapper;

    public TaskService(
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        ITaskValidationService validationService,
        IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
        _validationService = validationService;
        _mapper = mapper;
    }

    public async Task<Result<TaskDto>> GetByIdAsync(int taskId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct))
                return Result<TaskDto>.Failure(EntityError.Forbidden);

            var entity = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, ct);
            if (entity is null) return Result<TaskDto>.Failure(EntityError.NotFound(taskId));

            return Result<TaskDto>.Success(_mapper.Map<TaskDto>(entity));
        }
        catch
        {
            return Result<TaskDto>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<List<TaskDto>>> GetByProjectAsync(int projectId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, ct))
                return Result<List<TaskDto>>.Failure(EntityError.Forbidden);

            var list = await _unitOfWork.Tasks.GetByProjectAsync(projectId, companyId, ct);
            return Result<List<TaskDto>>.Success(_mapper.Map<List<TaskDto>>(list));
        }
        catch
        {
            return Result<List<TaskDto>>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<List<TaskDto>>> GetBacklogAsync(int projectId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, ct))
                return Result<List<TaskDto>>.Failure(EntityError.Forbidden);

            var list = await _unitOfWork.Tasks.GetBacklogAsync(projectId, companyId, ct);
            return Result<List<TaskDto>>.Success(_mapper.Map<List<TaskDto>>(list));
        }
        catch
        {
            return Result<List<TaskDto>>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<List<TaskDto>>> GetBySprintAsync(int sprintId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessSprintAsync(companyId, userId, sprintId, ct))
                return Result<List<TaskDto>>.Failure(EntityError.Forbidden);

            var list = await _unitOfWork.Tasks.GetBySprintAsync(sprintId, companyId, ct);
            return Result<List<TaskDto>>.Success(_mapper.Map<List<TaskDto>>(list));
        }
        catch
        {
            return Result<List<TaskDto>>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<int>> CreateAsync(CreateTaskDto dto, Guid companyId, string createdByUserId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, createdByUserId, dto.ProjectId, ct))
                return Result<int>.Failure(EntityError.Forbidden);

            var validation = await _validationService.ValidateTaskCreateAsync(companyId, dto, ct);
            if (!validation.IsSuccess || validation.Value is null)
                return Result<int>.Failure(validation.Errors.ToArray());

            var entity = _mapper.Map<TaskEntity>(dto);
            entity.CompanyId = companyId;
            entity.CreatedByUserId = createdByUserId;
            entity.CreatedAtUtc = DateTime.UtcNow;
            entity.AssigneeUserId = validation.Value.AssigneeUserId;
            entity.Status = validation.Value.Status;
            entity.Priority = validation.Value.Priority;

            await _unitOfWork.Tasks.AddAsync(entity, ct);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<int>.Failure(EntityError.CreationFailed);

            return Result<int>.Success(entity.Id);
        }
        catch
        {
            return Result<int>.Failure(EntityError.CreationUnexpectedError);
        }
    }

    public async Task<Result<TaskDto>> UpdateAsync(int taskId, UpdateTaskDto dto, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct))
                return Result<TaskDto>.Failure(EntityError.Forbidden);

            var entity = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, ct);
            if (entity is null) return Result<TaskDto>.Failure(EntityError.NotFound(taskId));

            var entityProjectId = entity.ProjectId;
            if (!entityProjectId.HasValue)
                return Result<TaskDto>.Failure("Task project is invalid.");

            var validation = await _validationService.ValidateTaskUpdateAsync(companyId, entityProjectId.Value, dto, ct);
            if (!validation.IsSuccess || validation.Value is null)
                return Result<TaskDto>.Failure(validation.Errors.ToArray());

            _mapper.Map(dto, entity);
            entity.AssigneeUserId = validation.Value.AssigneeUserId;
            entity.Status = validation.Value.Status;
            entity.Priority = validation.Value.Priority;

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<TaskDto>.Failure(EntityError.NoChangesDetected);

            return Result<TaskDto>.Success(_mapper.Map<TaskDto>(entity));
        }
        catch
        {
            return Result<TaskDto>.Failure(EntityError.UpdateUnexpectedError);
        }
    }

    public async Task<Result<bool>> DeleteAsync(int taskId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct))
                return Result<bool>.Failure(EntityError.Forbidden);

            var entity = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, ct);
            if (entity is null) return Result<bool>.Failure(EntityError.NotFound(taskId));

            await _unitOfWork.Tasks.Delete(entity, ct);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<bool>.Failure(EntityError.DeletionUnexpectedError);

            return Result<bool>.Success(true);
        }
        catch
        {
            return Result<bool>.Failure(EntityError.DeletionUnexpectedError);
        }
    }

    public async Task<Result<bool>> MoveToSprintAsync(int taskId, int sprintId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin)
            {
                var canAccessTask = await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct);
                var canAccessSprint = await _accessControl.CanAccessSprintAsync(companyId, userId, sprintId, ct);

                if (!canAccessTask || !canAccessSprint)
                    return Result<bool>.Failure(EntityError.Forbidden);
            }

            var moveValidation = await _validationService.ValidateTaskMoveToSprintAsync(companyId, taskId, sprintId, ct);
            if (!moveValidation.IsSuccess)
                return Result<bool>.Failure(moveValidation.Errors.ToArray());

            await _unitOfWork.Tasks.MoveToSprintAsync(taskId, sprintId, companyId, ct);
            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<bool>.Failure(EntityError.NoChangesDetected);

            return Result<bool>.Success(true);
        }
        catch (KeyNotFoundException)
        {
            return Result<bool>.Failure(EntityError.NotFound(taskId));
        }
        catch
        {
            return Result<bool>.Failure(EntityError.UpdateUnexpectedError);
        }
    }

    public async Task<Result<bool>> MoveToBacklogAsync(int taskId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct))
                return Result<bool>.Failure(EntityError.Forbidden);

            await _unitOfWork.Tasks.MoveToBacklogAsync(taskId, companyId, ct);
            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<bool>.Failure(EntityError.NoChangesDetected);

            return Result<bool>.Success(true);
        }
        catch (KeyNotFoundException)
        {
            return Result<bool>.Failure(EntityError.NotFound(taskId));
        }
        catch
        {
            return Result<bool>.Failure(EntityError.UpdateUnexpectedError);
        }
    }

    public async Task<Result<bool>> SetStatusAsync(int taskId, string status, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct))
                return Result<bool>.Failure(EntityError.Forbidden);

            var validation = await _validationService.ValidateTaskStatusAsync(status);
            if (!validation.IsSuccess || validation.Value is null)
                return Result<bool>.Failure(validation.Errors.ToArray());

            await _unitOfWork.Tasks.SetStatusAsync(taskId, companyId, validation.Value, ct);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<bool>.Failure(EntityError.NoChangesDetected);

            return Result<bool>.Success(true);
        }
        catch (KeyNotFoundException)
        {
            return Result<bool>.Failure(EntityError.NotFound(taskId));
        }
        catch
        {
            return Result<bool>.Failure(EntityError.UpdateUnexpectedError);
        }
    }

    public async Task<Result<bool>> AssignAsync(int taskId, string? assigneeUserId, Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct)
    {
        try
        {
            if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, ct))
                return Result<bool>.Failure(EntityError.Forbidden);

            var validation = await _validationService.ValidateTaskAssignmentForTaskAsync(companyId, assigneeUserId, taskId, ct);
            if (!validation.IsSuccess)
                return Result<bool>.Failure(validation.Errors.ToArray());

            await _unitOfWork.Tasks.AssignAsync(taskId, companyId, validation.Value, ct);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<bool>.Failure(EntityError.NoChangesDetected);

            return Result<bool>.Success(true);
        }
        catch (KeyNotFoundException)
        {
            return Result<bool>.Failure(EntityError.NotFound(taskId));
        }
        catch
        {
            return Result<bool>.Failure(EntityError.UpdateUnexpectedError);
        }
    }
}
