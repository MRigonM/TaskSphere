using TaskSphere.Application.DataTransferObjects.Identity;
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

public interface IAccountService
{
    Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken cancellationToken = default);
    Task<Result<string>> RegisterAsync(RegisterDto dto, CancellationToken cancellationToken = default);
    Task<Result<string>> CreateUserForCompanyAsync(RegisterDto dto, Guid companyId, CancellationToken ct = default);
}