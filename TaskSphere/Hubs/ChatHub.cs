using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Chat;

namespace TaskSphere.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IAccessControlService _accessControl;

    public ChatHub(IChatService chatService, IAccessControlService accessControl)
    {
        _chatService = chatService;
        _accessControl = accessControl;
    }

    public async Task JoinProject(int projectId)
    {
        var userId = GetUserId();
        var companyId = GetCompanyId();

        var hasAccess = await _accessControl.CanAccessProjectAsync(companyId, userId, projectId);
        if (!hasAccess)
        {
            await Clients.Caller.SendAsync("Error", "You do not have access to this project.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    public async Task LeaveProject(int projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    public async Task SendMessage(SendMessageDto dto)
    {
        var userId = GetUserId();
        var companyId = GetCompanyId();

        var result = await _chatService.SendMessageAsync(companyId, userId, dto);
        if (!result.IsSuccess)
        {
            await Clients.Caller.SendAsync("Error", result.Errors.First().Description);
            return;
        }

        await Clients.Group($"project-{dto.ProjectId}").SendAsync("ReceiveMessage", result.Value);
    }

    private string GetUserId() =>
        Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new HubException("User not authenticated.");

    private Guid GetCompanyId()
    {
        var claim = Context.User?.FindFirstValue("companyId");
        return claim is not null && Guid.TryParse(claim, out var id)
            ? id
            : throw new HubException("CompanyId not found in token.");
    }
}