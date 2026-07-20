namespace TaskSphere.Domain.Interfaces;

public interface IReadOnlyUnitOfWork
{
    IProjectRepository Projects { get; }
    ITaskRepository Tasks { get; }
    ISprintRepository Sprints { get; }
    IMemberRepository Members { get; }
    ICompanyRepository Companies { get; }
    IAuditRepository AuditLogs { get; }
}
