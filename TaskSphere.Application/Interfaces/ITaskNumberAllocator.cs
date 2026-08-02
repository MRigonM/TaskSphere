namespace TaskSphere.Application.Interfaces;

public interface ITaskNumberAllocator
{
    /// <summary>
    /// Atomically reserves the next task number for a project.
    /// Returns null when the project does not exist.
    /// </summary>
    Task<int?> AllocateAsync(int projectId, CancellationToken ct);
}
