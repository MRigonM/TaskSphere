export interface AgentDto { id: string; name: string; }
export interface AgentTokenInfoDto { prefix: string; createdAtUtc: string; lastUsedAtUtc: string | null; }
export interface AgentStatusDto { hasAgent: boolean; agentName: string | null; token: AgentTokenInfoDto | null; }
export interface IssuedAgentTokenDto { token: string; prefix: string; createdAtUtc: string; }
