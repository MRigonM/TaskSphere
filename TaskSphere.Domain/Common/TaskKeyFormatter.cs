using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Common;

public static class TaskKeyFormatter
{
    /// <summary>
    /// Renders a task key. Branches on ProjectId rather than Project so that a genuine
    /// orphan (null key) is distinguishable from a forgotten .Include (throws).
    /// </summary>
    public static string? Format(int? projectId, Project? project, int number)
    {
        if (projectId is null)
            return null;

        if (project is null)
            throw new InvalidOperationException(
                $"Task has ProjectId {projectId} but Project was not loaded. " +
                "Add .Include(t => t.Project) to the repository query.");

        return $"{project.Key}-{number}";
    }
}
