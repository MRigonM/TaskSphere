using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class CompanyRepository: GenericRepository<Company, Guid>, ICompanyRepository
{
    private readonly ApplicationDbContext _context;

    public CompanyRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }
}