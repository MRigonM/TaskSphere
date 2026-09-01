using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Company;

namespace TaskSphere.Application.Interfaces;

public interface ICompanyService
{
    Task<Result<CompanyDto>> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one company. Added for the invitation email, which names the company the member is
    /// being added to.
    /// </summary>
    Task<Result<CompanyDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}