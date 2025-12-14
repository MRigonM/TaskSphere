using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using Task = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies { get; set; }
    public DbSet<Member> Members { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Sprint> Sprints { get; set; }
    public DbSet<Task> Tasks { get; set; }

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

            entity.Property(t => t.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("ToDo");

            entity.Property(t => t.Priority)
                .HasMaxLength(50);

            entity.HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}