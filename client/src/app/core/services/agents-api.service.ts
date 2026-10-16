import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AgentDto, AgentStatusDto, IssuedAgentTokenDto } from '../models/agents.models';

@Injectable({ providedIn: 'root' })
export class AgentsApiService {
  private base = `${environment.apiUrl}agents/me`;

  constructor(private http: HttpClient) {}

  getMine(): Observable<AgentStatusDto> {
    return this.http.get<AgentStatusDto>(this.base);
  }

  create(): Observable<AgentDto> {
    return this.http.post<AgentDto>(this.base, {});
  }

  regenerateToken(): Observable<IssuedAgentTokenDto> {
    return this.http.post<IssuedAgentTokenDto>(`${this.base}/token`, {});
  }

  revokeToken(): Observable<string> {
    return this.http.delete(`${this.base}/token`, { responseType: 'text' });
  }
}
