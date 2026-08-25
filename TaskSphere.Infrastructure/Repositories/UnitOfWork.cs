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

    private IGitHubInstallationRepository? _gitHubInstallations;
    public IGitHubInstallationRepository GitHubInstallations =>
        _gitHubInstallations ??= new GitHubInstallationRepository(_context);

    private IGitHubRepositoryRepository? _gitHubRepositories;
    public IGitHubRepositoryRepository GitHubRepositories =>
        _gitHubRepositories ??= new GitHubRepositoryRepository(_context);

    private IProjectRepositoryLinkRepository? _projectRepositoryLinks;
    public IProjectRepositoryLinkRepository ProjectRepositoryLinks =>
        _projectRepositoryLinks ??= new ProjectRepositoryLinkRepository(_context);

    private IGitHubCommitRepository? _gitHubCommits;
    public IGitHubCommitRepository GitHubCommits =>
        _gitHubCommits ??= new GitHubCommitRepository(_context);

    private IGitHubBranchRepository? _gitHubBranches;
    public IGitHubBranchRepository GitHubBranches =>
        _gitHubBranches ??= new GitHubBranchRepository(_context);

    private IGitHubPullRequestRepository? _gitHubPullRequests;
    public IGitHubPullRequestRepository GitHubPullRequests =>
        _gitHubPullRequests ??= new GitHubPullRequestRepository(_context);

    private ITaskLinkRepository? _taskLinks;
    public ITaskLinkRepository TaskLinks =>
        _taskLinks ??= new TaskLinkRepository(_context);

    private IGenericRepository<ChatMessage, int>? _chatMessages;
    public IGenericRepository<ChatMessage, int> ChatMessages =>
        _chatMessages ??= new GenericRepository<ChatMessage, int>(_context);
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken) => 
        await _context.SaveChangesAsync(cancellationToken);

    public void DiscardPendingChanges()
    {
        // Only what is dirty: entities read and left untouched stay tracked, so a caller that
        // continues after the failure keeps its snapshot. Detaching a Modified entity drops
        // the write EF would otherwise re-send on the next save.
        var pending = _context.ChangeTracker
            .Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in pending)
            entry.State = EntityState.Detached;
    }
}