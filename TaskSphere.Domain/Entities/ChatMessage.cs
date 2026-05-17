using TaskSphere.Domain.Entities.Identity;

namespace TaskSphere.Domain.Entities;

public class ChatMessage : BaseEntity<int>
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string SenderId { get; set; } = string.Empty;
    public AppUser Sender { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}