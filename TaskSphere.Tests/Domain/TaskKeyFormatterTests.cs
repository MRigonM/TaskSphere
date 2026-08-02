using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Tests.Domain;

public class TaskKeyFormatterTests
{
    [Fact]
    public void Format_ReturnsKey_WhenProjectIsLoaded()
    {
        var project = new Project { Id = 1, Name = "TaskSphere", Key = "TS" };
        Assert.Equal("TS-42", TaskKeyFormatter.Format(1, project, 42));
    }

    [Fact]
    public void Format_ReturnsNull_ForOrphanTask()
    {
        Assert.Null(TaskKeyFormatter.Format(null, null, 0));
    }

    [Fact]
    public void Format_Throws_WhenProjectIdIsSetButProjectIsNotLoaded()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TaskKeyFormatter.Format(1, null, 42));
        Assert.Contains("Include", ex.Message, StringComparison.Ordinal);
    }
}
