using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    private IProjectRepository? _projects;
    public IProjectRepository Projects => _projects ??= new ProjectRepository(_context);

    private ITaskRepository? _tasks;
    public ITaskRepository Tasks => _tasks ??= new TaskRepository(_context);

    private ISprintRepository? _sprints;
    public ISprintRepository Sprints => _sprints ??= new SprintRepository(_context);

    private IMemberRepository? _members;
    public IMemberRepository Members => _members ??= new MemberRepository(_context);

    private ICompanyRepository? _companies;
    public ICompanyRepository Companies => _companies ??= new CompanyRepository(_context);

    private IAuditRepository? _auditLogs;
    public IAuditRepository AuditLogs => _auditLogs ??= new AuditRepository(_context);

    private IGenericRepository<ChatMessage, int>? _chatMessages;
    public IGenericRepository<ChatMessage, int> ChatMessages =>
        _chatMessages ??= new GenericRepository<ChatMessage, int>(_context);
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken) => 
        await _context.SaveChangesAsync(cancellationToken);
}