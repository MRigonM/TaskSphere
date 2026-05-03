using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Chat;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class ChatService : IChatService
{
    private readonly IGenericRepository<ChatMessage, int> _chatRepo;
    private readonly IAccessControlService _accessControl;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _userManager;

    public ChatService(
        IGenericRepository<ChatMessage, int> chatRepo,
        IAccessControlService accessControl,
        IUnitOfWork unitOfWork,
        UserManager<AppUser> userManager)
    {
        _chatRepo = chatRepo;
        _accessControl = accessControl;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    public async Task<Result<ChatMessageDto>> SendMessageAsync(Guid companyId, string userId, SendMessageDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Content) && string.IsNullOrWhiteSpace(dto.ImageUrl))
            return Result<ChatMessageDto>.Failure("Message content cannot be empty.");

        var hasAccess = await _accessControl.CanAccessProjectAsync(companyId, userId, dto.ProjectId, ct);
        if (!hasAccess)
            return Result<ChatMessageDto>.Failure(new Error("Auth.Forbidden", "You do not have access to this project."));

        var message = new ChatMessage
        {
            ProjectId = dto.ProjectId,
            SenderId = userId,
            Content = dto.Content?.Trim() ?? string.Empty,
            ImageUrl = dto.ImageUrl
        };

        await _chatRepo.AddAsync(message, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var sender = await _userManager.FindByIdAsync(userId);
        var result = new ChatMessageDto(
            message.Id,
            message.ProjectId,
            message.SenderId,
            sender?.Name ?? "Unknown",
            message.Content,
            message.ImageUrl,
            message.CreatedAtUtc);

        return Result<ChatMessageDto>.Success(result);
    }

    public async Task<Result<PagedResult<ChatMessageDto>>> GetMessagesAsync(Guid companyId, string userId, int projectId, int page, int pageSize, CancellationToken ct = default)
    {
        var hasAccess = await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, ct);
        if (!hasAccess)
            return Result<PagedResult<ChatMessageDto>>.Failure(new Error("Auth.Forbidden", "You do not have access to this project."));

        var query = _chatRepo.GetAll()
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.CreatedAtUtc);

        var totalCount = await query.CountAsync(ct);

        var messages = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(m => m.Sender)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.ProjectId,
                m.SenderId,
                m.Sender.Name,
                m.Content,
                m.ImageUrl,
                m.CreatedAtUtc))
            .ToListAsync(ct);

        var pagedResult = new PagedResult<ChatMessageDto>(messages, totalCount, page, pageSize);
        return Result<PagedResult<ChatMessageDto>>.Success(pagedResult);
    }
}