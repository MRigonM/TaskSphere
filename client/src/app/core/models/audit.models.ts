export interface AuditLogDto {
  id: number;
  timestamp: string;
  username: string | null;
  httpMethod: string | null;
  path: string;
  ip: string | null;
  action: string;
  requestData: string | null;
  statusCode: number;
  durationMs: number;
}

export interface AuditQueryDto {
  username?: string;
  action?: string;
  httpMethod?: string;
  page: number;
  pageSize: number;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface EndpointStatDto {
  action: string;
  count: number;
}

export interface DailyStatDto {
  date: string;
  count: number;
}

export interface AuditStatsDto {
  totalRequests: number;
  activeUsers: number;
  topEndpoints: EndpointStatDto[];
  requestsPerDay: DailyStatDto[];
}
