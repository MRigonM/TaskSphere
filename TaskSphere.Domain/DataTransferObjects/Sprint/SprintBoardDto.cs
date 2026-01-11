using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Domain.DataTransferObjects.Sprint;
public class SprintBoardDto
{
    public int SprintId { get; set; }
    public string SprintName { get; set; } = "";
    public int? ProjectId { get; set; }
    public List<TaskEntity> Open { get; set; } = [];
    public List<TaskEntity> InProgress { get; set; } = [];
    public List<TaskEntity> Blocked { get; set; } = [];
    public List<TaskEntity> Done { get; set; } = [];
    public List<TaskEntity> Low { get; set; } = [];
    public List<TaskEntity> Medium { get; set; } = [];
    public List<TaskEntity> High { get; set; } = [];
    public List<TaskEntity> Critical { get; set; } = [];
}