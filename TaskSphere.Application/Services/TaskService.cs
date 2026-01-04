using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Interfaces;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Application.Services;

public class TaskService : ITaskService
{
    private readonly ITaskRepository _taskRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public TaskService(ITaskRepository taskRepository, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _taskRepository = taskRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }
    
    public async Task<Result<TaskDto>> GetByIdAsync(int taskId, Guid companyId, CancellationToken ct)
    {
        try
        {
            var entity = await _taskRepository.GetByIdForCompanyAsync(taskId, companyId, ct);
            if (entity is null) return Result<TaskDto>.Failure(EntityError.NotFound(taskId));

            return Result<TaskDto>.Success(_mapper.Map<TaskDto>(entity));
        }
        catch
        {
            return Result<TaskDto>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<List<TaskDto>>> GetByProjectAsync(int projectId, Guid companyId, CancellationToken ct)
    {
        try
        {
            var list = await _taskRepository.GetByProjectAsync(projectId, companyId, ct);
            return Result<List<TaskDto>>.Success(_mapper.Map<List<TaskDto>>(list));
        }
        catch
        {
            return Result<List<TaskDto>>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<List<TaskDto>>> GetBacklogAsync(int projectId, Guid companyId, CancellationToken ct)
    {
        try
        {
            var list = await _taskRepository.GetBacklogAsync(projectId, companyId, ct);
            return Result<List<TaskDto>>.Success(_mapper.Map<List<TaskDto>>(list));
        }
        catch
        {
            return Result<List<TaskDto>>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<List<TaskDto>>> GetBySprintAsync(int sprintId, Guid companyId, CancellationToken ct)
    {
        try
        {
            var list = await _taskRepository.GetBySprintAsync(sprintId, companyId, ct);
            return Result<List<TaskDto>>.Success(_mapper.Map<List<TaskDto>>(list));
        }
        catch
        {
            return Result<List<TaskDto>>.Failure(EntityError.RetrievalError);
        }
    }

    public async Task<Result<int>> CreateAsync(CreateTaskDto dto, Guid companyId, string createdByUserId, CancellationToken ct)
    {
        try
        {
            var entity = _mapper.Map<TaskEntity>(dto);
            entity.CompanyId = companyId;
            entity.CreatedByUserId = createdByUserId;
            entity.CreatedAtUtc = DateTime.UtcNow;

            await _taskRepository.AddAsync(entity, ct);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<int>.Failure(EntityError.CreationFailed);

            return Result<int>.Success(entity.Id);
        }
        catch
        {
            return Result<int>.Failure(EntityError.CreationUnexpectedError);
        }
    }

    public async Task<Result<TaskDto>> UpdateAsync(int taskId, UpdateTaskDto dto, Guid companyId, CancellationToken ct)
    {
        try
        {
            var entity = await _taskRepository.GetByIdForCompanyAsync(taskId, companyId, ct);
            if (entity is null) return Result<TaskDto>.Failure(EntityError.NotFound(taskId));

            _mapper.Map(dto, entity);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<TaskDto>.Failure(EntityError.NoChangesDetected);

            return Result<TaskDto>.Success(_mapper.Map<TaskDto>(entity));
        }
        catch
        {
            return Result<TaskDto>.Failure(EntityError.UpdateUnexpectedError);
        }
    }

    public async Task<Result<bool>> DeleteAsync(int taskId, Guid companyId, CancellationToken ct)
    {
        try
        {
            var entity = await _taskRepository.GetByIdForCompanyAsync(taskId, companyId, ct);
            if (entity is null) return Result<bool>.Failure(EntityError.NotFound(taskId));

            await _taskRepository.Delete(entity, ct);

            var saved = await _unitOfWork.SaveChangesAsync(ct);
            if (saved <= 0) return Result<bool>.Failure(EntityError.DeletionUnexpectedError);

            return Result<bool>.Success(true);
        }
        catch
        {
            return Result<bool>.Failure(EntityError.DeletionUnexpectedError);
        }
    }

    public async Task<Result<bool>> MoveToSprintAsync(int taskId, int sprintId, Guid companyId, CancellationToken ct)
    {
        try
        {
            await _taskRepository.MoveToSprintAsync(taskId, sprintId, companyId, ct);
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

    public async Task<Result<bool>> MoveToBacklogAsync(int taskId, Guid companyId, CancellationToken ct)
    {
        try
        {
            await _taskRepository.MoveToBacklogAsync(taskId, companyId, ct);
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

    public async Task<Result<bool>> SetStatusAsync(int taskId, string status, Guid companyId, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(status))
                return Result<bool>.Failure(new Error("Validation.StatusRequired", "Status is required."));

            await _taskRepository.SetStatusAsync(taskId, companyId, status, ct);

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

    public async Task<Result<bool>> AssignAsync(int taskId, string? assigneeUserId, Guid companyId, CancellationToken ct)
    {
        try
        {
            await _taskRepository.AssignAsync(taskId, companyId, assigneeUserId, ct);

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