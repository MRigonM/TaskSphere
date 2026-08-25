import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AddMemberDto, CreateProjectDto, MemberDto, ProjectDto, ProjectActivityRefreshDto } from '../../core/models/projects.models';
import {environment} from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ProjectsApiService {
  private base = `${environment.apiUrl}Projects/`;

  constructor(private http: HttpClient) {}

  create(dto: CreateProjectDto): Observable<ProjectDto> {
    return this.http.post<ProjectDto>(`${this.base}`, dto);
  }

  getAll(): Observable<ProjectDto[]> {
    return this.http.get<ProjectDto[]>(`${this.base}`);
  }

  getMembers(projectId: number): Observable<MemberDto[]> {
    return this.http.get<MemberDto[]>(`${this.base}${projectId}/members`);
  }

  getById(projectId: number): Observable<ProjectDto> {
    return this.http.get<ProjectDto>(`${this.base}${projectId}`);
  }

  /**
   * The only mutation a project has. The body carries the toggle alone — the server DTO is
   * deliberately narrow so this cannot become a way to edit the project key.
   */
  updateSettings(projectId: number, autoDoneOnMerge: boolean): Observable<ProjectDto> {
    return this.http.patch<ProjectDto>(`${this.base}${projectId}/settings`, { autoDoneOnMerge });
  }

  getMembersProjects(): Observable<ProjectDto[]> {
    return this.http.get<ProjectDto[]>(`${this.base}mine`);
  }

  addMember(projectId: number, dto: AddMemberDto): Observable<string> {
    return this.http.post(`${this.base}${projectId}/members`, dto, { responseType: 'text' });
  }

  removeMember(projectId: number, userId: string): Observable<string> {
    return this.http.delete(`${this.base}${projectId}/members/${userId}`, { responseType: 'text' });
  }

  /**
   * Refreshes this project's pull requests and applies any merge → Done transitions. Fired on
   * board and backlog load; failures are swallowed by the caller on purpose.
   */
  refreshGitHub(projectId: number): Observable<ProjectActivityRefreshDto> {
    return this.http.post<ProjectActivityRefreshDto>(`${this.base}${projectId}/github-refresh`, {});
  }
}
