using TaskSphere.Application.DataTransferObjects.Company;
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

public interface ICompanyService
{
    Task<Result<CompanyDto>> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default);
}