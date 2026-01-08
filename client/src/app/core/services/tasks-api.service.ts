import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateTaskDto, TaskDto, UpdateTaskDto } from '../models/tasks.models';

@Injectable({ providedIn: 'root' })
export class TasksApiService {
  private base = `${environment.apiUrl}Tasks/`;

  constructor(private http: HttpClient) {}

  getById(taskId: number): Observable<TaskDto> {
    return this.http.get<TaskDto>(`${this.base}${taskId}`);
  }

  getByProject(projectId: number): Observable<TaskDto[]> {
    return this.http.get<TaskDto[]>(`${this.base}project/${projectId}`);
  }

  getBacklog(projectId: number): Observable<TaskDto[]> {
    return this.http.get<TaskDto[]>(`${this.base}project/${projectId}/backlog`);
  }

  getBySprint(sprintId: number): Observable<TaskDto[]> {
    return this.http.get<TaskDto[]>(`${this.base}sprint/${sprintId}`);
  }

  create(dto: CreateTaskDto): Observable<number> {
    // backend returns Result<int> (task id)
    return this.http.post<number>(`${this.base}`, dto);
  }

  update(taskId: number, dto: UpdateTaskDto): Observable<TaskDto> {
    return this.http.put<TaskDto>(`${this.base}${taskId}`, dto);
  }

  delete(taskId: number): Observable<any> {
    return this.http.delete(`${this.base}${taskId}`);
  }

  moveToSprint(taskId: number, sprintId: number): Observable<any> {
    return this.http.patch(`${this.base}${taskId}/move-to-sprint/${sprintId}`, null);
  }

  moveToBacklog(taskId: number): Observable<any> {
    return this.http.patch(`${this.base}${taskId}/move-to-backlog`, null);
  }

  setStatus(taskId: number, status: string): Observable<any> {
    return this.http.patch(`${this.base}${taskId}/status?status=${encodeURIComponent(status)}`, null);
  }

  assign(taskId: number, assigneeUserId: string | null): Observable<any> {
    const q = assigneeUserId ? encodeURIComponent(assigneeUserId) : '';
    return this.http.patch(`${this.base}${taskId}/assignee?assigneeUserId=${q}`, null);
  }
}
