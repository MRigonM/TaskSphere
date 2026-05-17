namespace TaskSphere.Domain.DataTransferObjects.Chat;

public record ChatMessageDto(int Id, int ProjectId, string SenderId, string SenderName, string Content, string? ImageUrl, DateTime SentAt);

public record SendMessageDto(int ProjectId, string Content, string? ImageUrl = null);