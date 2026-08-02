namespace TaskSphere.Domain.DataTransferObjects.Project;

public record CreateProjectDto(string Name, string Key);

public record ProjectDto(int Id, string Name, string Key);

public record AddMemberDto(string UserId);

public record MemberDto(int Id, int ProjectId, string UserId, string UserName, string Email);
