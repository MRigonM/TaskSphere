using TaskSphere.Application.DataTransferObjects.Identity;
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

public interface IAccountService
{
    Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken cancellationToken = default);
    Task<Result<string>> RegisterAsync(RegisterDto dto, CancellationToken cancellationToken = default);
    Task<Result<string>> CreateUserForCompanyAsync(RegisterDto dto, Guid companyId, CancellationToken ct = default);
    Task<Result<IEnumerable<UserDto>>> GetUsersAsync(Guid companyId, UserQueryDto query, CancellationToken ct = default);
    Task<Result<UserDto>> UpdateUserAsync(Guid companyId, string userId, UpdateUserDto dto, CancellationToken ct = default);
    Task<Result<string>> DeleteUserAsync(Guid companyId, string userId, CancellationToken ct = default);

}