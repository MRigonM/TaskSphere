namespace TaskSphere.Application.DataTransferObjects.Project;

public record CreateProjectDto(string Name);

public record ProjectDto(int Id, string Name);

public record AddMemberDto(string UserId);

public record MemberDto(int Id, int ProjectId, string UserId, string UserName, string Email);
