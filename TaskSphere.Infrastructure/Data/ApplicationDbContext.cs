using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using Task = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public override async SystemTask.Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAtUtc") is not null)
                entry.Property("CreatedAtUtc").CurrentValue = now;

            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty("UpdatedAtUtc") is not null)
                entry.Property("UpdatedAtUtc").CurrentValue = now;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    public DbSet<Company> Companies { get; set; }
    public DbSet<Member> Members { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Sprint> Sprints { get; set; }
    public DbSet<Task> Tasks { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<GitHubInstallation> GitHubInstallations { get; set; }
    public DbSet<GitHubRepository> GitHubRepositories { get; set; }
    public DbSet<ProjectRepositoryLink> ProjectRepositoryLinks { get; set; }
    public DbSet<GitHubCommit> GitHubCommits { get; set; }
    public DbSet<GitHubBranch> GitHubBranches { get; set; }
    public DbSet<GitHubPullRequest> GitHubPullRequests { get; set; }
    public DbSet<GitHubBranchCommit> GitHubBranchCommits { get; set; }
    public DbSet<TaskLink> TaskLinks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasQueryFilter(u => !u.IsDeleted);

            entity.Property(u => u.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasOne(u => u.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(u => u.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasQueryFilter(c => !c.IsDeleted);

            entity.Property(c => c.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWID()");

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasMany(c => c.Projects)
                .WithOne(p => p.Company)
                .HasForeignKey(p => p.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(c => c.Sprints)
                .WithOne(s => s.Company)
                .HasForeignKey(s => s.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasQueryFilter(p => !p.IsDeleted);

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(300);

            entity.Property(p => p.Key)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("");

            entity.Property(p => p.NextTaskNumber)
                .IsRequired()
                .HasDefaultValue(1);

            entity.HasIndex(p => new { p.CompanyId, p.Key })
                .IsUnique();

            entity.HasOne(p => p.Company)
                .WithMany(c => c.Projects)
                .HasForeignKey(p => p.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.Members)
                .WithOne(m => m.Project)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.Sprints)
                .WithOne(s => s.Project)
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.Tasks)
                .WithOne(t => t.Project)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Member>(entity =>
        {
            entity.HasQueryFilter(m => !m.IsDeleted);

            entity.HasOne(m => m.Project)
                .WithMany(p => p.Members)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => new
                {
                    m.ProjectId, m.UserId
                })
                .IsUnique();
        });

        modelBuilder.Entity<Sprint>(entity =>
        {
            entity.HasQueryFilter(s => !s.IsDeleted);

            entity.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(s => s.StartDate)
                .IsRequired();

            entity.Property(s => s.EndDate)
                .IsRequired();

            entity.HasOne(s => s.Project)
                .WithMany(p => p.Sprints)
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Company)
                .WithMany(c => c.Sprints)
                .HasForeignKey(s => s.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.HasQueryFilter(t => !t.IsDeleted);

            entity.Property(t => t.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(t => t.Number)
                .IsRequired()
                .HasDefaultValue(0);
            
            entity.HasIndex(t => new { t.ProjectId, t.Number })
                .IsUnique()
                .HasFilter("[ProjectId] IS NOT NULL");

            entity.Property(t => t.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Open");

            entity.Property(t => t.Priority)
                .HasMaxLength(50);

            entity.HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasQueryFilter(m => !m.IsDeleted);

            entity.Property(m => m.Content)
                .HasMaxLength(2000);

            entity.Property(m => m.ImageUrl)
                .HasMaxLength(500);

            entity.HasOne(m => m.Project)
                .WithMany()
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(a => a.CompanyId);
        });

        modelBuilder.Entity<GitHubInstallation>(entity =>
        {
            entity.HasQueryFilter(i => !i.IsDeleted);

            entity.Property(i => i.AccountLogin)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(i => i.AccountType)
                .IsRequired()
                .HasMaxLength(20);

            // Deliberately not filtered on IsDeleted: GitHub issues a new InstallationId
            // on reinstall, so ids never recycle. Lookups must use IgnoreQueryFilters().
            entity.HasIndex(i => i.InstallationId)
                .IsUnique();

            entity.HasOne(i => i.Company)
                .WithMany()
                .HasForeignKey(i => i.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GitHubRepository>(entity =>
        {
            entity.HasQueryFilter(r => !r.IsDeleted);

            entity.Property(r => r.FullName)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(r => r.DefaultBranch)
                .HasMaxLength(255);

            entity.HasIndex(r => r.RepositoryId)
                .IsUnique();

            entity.HasOne(r => r.Installation)
                .WithMany(i => i.Repositories)
                .HasForeignKey(r => r.GitHubInstallationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectRepositoryLink>(entity =>
        {
            entity.HasQueryFilter(l => !l.IsDeleted);

            entity.Property(l => l.LinkedByUserId)
                .IsRequired()
                .HasMaxLength(450);

            // Filtered, unlike the two above: unlink then relink must be legal.
            entity.HasIndex(l => new { l.ProjectId, l.GitHubRepositoryId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasOne(l => l.Project)
                .WithMany()
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.Repository)
                .WithMany()
                .HasForeignKey(l => l.GitHubRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GitHubCommit>(entity =>
        {
            entity.HasQueryFilter(c => !c.IsDeleted);

            entity.Property(c => c.Sha)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(c => c.Message)
                .IsRequired()
                .HasMaxLength(4000);

            entity.Property(c => c.AuthorName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(c => c.AuthorLogin)
                .HasMaxLength(255);

            entity.Property(c => c.HtmlUrl)
                .IsRequired()
                .HasMaxLength(1000);

            // Unfiltered, per B1's rule for GitHub-issued identities: upserts revive the
            // soft-deleted row rather than inserting a second one beside it.
            entity.HasIndex(c => new { c.GitHubRepositoryId, c.Sha })
                .IsUnique()
                .HasDatabaseName("IX_GitHubCommits_RepositoryId_Sha");

            entity.HasOne(c => c.Repository)
                .WithMany()
                .HasForeignKey(c => c.GitHubRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GitHubBranch>(entity =>
        {
            entity.HasQueryFilter(b => !b.IsDeleted);

            entity.Property(b => b.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(b => b.HeadSha)
                .IsRequired()
                .HasMaxLength(40);

            // Unfiltered, and this is where it earns its keep: merge TS-42-fix, GitHub deletes
            // the branch, recreate it a week later. A filtered index would admit a second live
            // row alongside the soft-deleted one.
            entity.HasIndex(b => new { b.GitHubRepositoryId, b.Name })
                .IsUnique()
                .HasDatabaseName("IX_GitHubBranches_RepositoryId_Name");

            entity.HasOne(b => b.Repository)
                .WithMany()
                .HasForeignKey(b => b.GitHubRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GitHubPullRequest>(entity =>
        {
            entity.HasQueryFilter(p => !p.IsDeleted);

            entity.Property(p => p.Title)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(p => p.Body)
                .HasMaxLength(8000);

            entity.Property(p => p.AuthorLogin)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(p => p.HeadBranch)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(p => p.HtmlUrl)
                .IsRequired()
                .HasMaxLength(1000);

            entity.HasIndex(p => new { p.GitHubRepositoryId, p.Number })
                .IsUnique()
                .HasDatabaseName("IX_GitHubPullRequests_RepositoryId_Number");

            entity.HasOne(p => p.Repository)
                .WithMany()
                .HasForeignKey(p => p.GitHubRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskLink>(entity =>
        {
            entity.HasQueryFilter(l => !l.IsDeleted);

            // Filtered on IsDeleted as well as the FK, matching ProjectRepositoryLink: a
            // TaskLink is a TaskSphere-owned row, not a GitHub identity, so a soft-deleted one
            // must never block a new link between the same pair.
            entity.HasIndex(l => new { l.TaskId, l.GitHubCommitId })
                .IsUnique()
                .HasFilter("[GitHubCommitId] IS NOT NULL AND [IsDeleted] = 0")
                .HasDatabaseName("IX_TaskLinks_TaskId_CommitId");

            entity.HasIndex(l => new { l.TaskId, l.GitHubBranchId })
                .IsUnique()
                .HasFilter("[GitHubBranchId] IS NOT NULL AND [IsDeleted] = 0")
                .HasDatabaseName("IX_TaskLinks_TaskId_BranchId");

            entity.HasIndex(l => new { l.TaskId, l.GitHubPullRequestId })
                .IsUnique()
                .HasFilter("[GitHubPullRequestId] IS NOT NULL AND [IsDeleted] = 0")
                .HasDatabaseName("IX_TaskLinks_TaskId_PullRequestId");

            entity.HasOne(l => l.Task)
                .WithMany()
                .HasForeignKey(l => l.TaskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<GitHubCommit>()
                .WithMany()
                .HasForeignKey(l => l.GitHubCommitId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<GitHubBranch>()
                .WithMany()
                .HasForeignKey(l => l.GitHubBranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<GitHubPullRequest>()
                .WithMany()
                .HasForeignKey(l => l.GitHubPullRequestId)
                .OnDelete(DeleteBehavior.Restrict);

            // A SECOND relationship to GitHubBranch on a different FK. Distinct from the
            // GitHubBranchId one above — that says "this link IS a branch", this says "this
            // link came VIA a branch".
            entity.HasOne<GitHubBranch>()
                .WithMany()
                .HasForeignKey(l => l.ViaGitHubBranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GitHubBranchCommit>(entity =>
        {
            entity.HasQueryFilter(bc => !bc.IsDeleted);

            // Filtered, following TaskLink rather than the mirror tables: this is a derived
            // TaskSphere row, not a GitHub identity, so nothing needs to be revived and no
            // lookup in this slice needs IgnoreQueryFilters.
            entity.HasIndex(bc => new { bc.GitHubBranchId, bc.GitHubCommitId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_GitHubBranchCommits_BranchId_CommitId");

            entity.HasOne(bc => bc.Branch)
                .WithMany()
                .HasForeignKey(bc => bc.GitHubBranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(bc => bc.Commit)
                .WithMany()
                .HasForeignKey(bc => bc.GitHubCommitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var createdAt = entityType.FindProperty("CreatedAtUtc");
            if (createdAt is not null)
                createdAt.SetDefaultValueSql("GETUTCDATE()");
        }
    }
}