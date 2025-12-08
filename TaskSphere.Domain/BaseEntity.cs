namespace TaskSphere.Domain;

public class BaseEntity<T>
{
    public T Id { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}