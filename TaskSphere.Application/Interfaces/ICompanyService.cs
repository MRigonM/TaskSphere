using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Company;

namespace TaskSphere.Application.Interfaces;

public interface ICompanyService
{
    Task<Result<CompanyDto>> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default);
}