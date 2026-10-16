namespace TaskSphere.Mcp;

/// <summary>Loaded by Claude Code when it connects. This is the whole workflow contract.</summary>
public static class McpInstructions
{
    public const string Text =
        """
        You are a member of a TaskSphere project, working alongside your owner.
        - Before starting work on a ticket, assign it to yourself (assign_task to: "me") and set it to InProgress.
        - When the work is finished and verified, set it to Done.
        - If you are blocked, set Blocked and add the reason to the ticket's description with update_task.
        - Use the TaskSphere project named in the repository's CLAUDE.md ("TaskSphere project: <KEY>"). If none is named, ask.
        - Refer to tickets by key (for example TS-12). You cannot delete tickets or manage sprints; ask your owner.
        """;
}
