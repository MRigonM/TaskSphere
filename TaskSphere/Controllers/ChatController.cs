using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize]
[RequireCompany]
public class ChatController : ApiBaseController
{
    private readonly IChatService _chatService;
    private readonly IWebHostEnvironment _env;

    public ChatController(IChatService chatService, IWebHostEnvironment env)
    {
        _chatService = chatService;
        _env = env;
    }

    [HttpGet("projects/{projectId:int}/messages")]
    public async Task<IActionResult> GetMessages(int projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _chatService.GetMessagesAsync(CompanyId, UserId, projectId, page, pageSize, ct);
        return FromResult(result);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest("Only image files (jpeg, png, gif, webp) are allowed.");

        var uploadsDir = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "chat");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        var url = $"/uploads/chat/{fileName}";
        return Ok(new { url });
    }
}