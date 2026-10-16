namespace TaskSphere.Domain.DataTransferObjects.Agent;

public record AgentDto(string Id, string Name);

public record AgentTokenInfoDto(string Prefix, DateTime CreatedAtUtc, DateTime? LastUsedAtUtc);

/// <summary>What the "My Claude" page renders. Never carries the token itself.</summary>
public record AgentStatusDto(bool HasAgent, string? AgentName, AgentTokenInfoDto? Token);

/// <summary>The only response that ever carries a plaintext token, and only once.</summary>
public record IssuedAgentTokenDto(string Token, string Prefix, DateTime CreatedAtUtc);

/// <summary>Who a valid token belongs to — what the auth handler turns into claims.</summary>
public record AgentIdentity(string AgentUserId, string Name, Guid CompanyId);
