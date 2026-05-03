using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Chat;

namespace TaskSphere.Application.Interfaces;

public interface IChatService
{
    Task<Result<ChatMessageDto>> SendMessageAsync(Guid companyId, string userId, SendMessageDto dto, CancellationToken ct = default);
    Task<Result<PagedResult<ChatMessageDto>>> GetMessagesAsync(Guid companyId, string userId, int projectId, int page, int pageSize, CancellationToken ct = default);
}