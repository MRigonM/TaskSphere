namespace TaskSphere.Domain.Interfaces;

public interface ISoftDeletion
{
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public void Undo()
    {
        IsDeleted = false;
        DeletedAt = null;
    }
}