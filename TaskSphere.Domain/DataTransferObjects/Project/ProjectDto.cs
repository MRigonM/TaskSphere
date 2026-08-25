namespace TaskSphere.Domain.DataTransferObjects.Project;

public record CreateProjectDto(string Name, string Key);

public record ProjectDto(int Id, string Name, string Key, bool AutoDoneOnMerge);

/// <summary>
/// Deliberately NOT a general project-update shape. Project.Key is uppercase-and-load-bearing:
/// changing it orphans every existing task key and silently breaks TaskKeyScanner.
/// </summary>
public record UpdateProjectSettingsDto(bool AutoDoneOnMerge);

public record AddMemberDto(string UserId);

public record MemberDto(int Id, int ProjectId, string UserId, string UserName, string Email);
