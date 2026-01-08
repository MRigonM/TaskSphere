import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateSprintDto,
  UpdateSprintDto,
  SprintDto,
  SprintBoardDto
} from '../models/sprints.models';

@Injectable({ providedIn: 'root' })
export class SprintsApiService {
  private base = `${environment.apiUrl}Sprints/`;

  constructor(private http: HttpClient) {}

  getByProject(projectId: number): Observable<SprintDto[]> {
    return this.http.get<SprintDto[]>(`${this.base}project/${projectId}`);
  }

  getById(sprintId: number): Observable<SprintDto> {
    return this.http.get<SprintDto>(`${this.base}${sprintId}`);
  }

  create(dto: CreateSprintDto): Observable<SprintDto> {
    return this.http.post<SprintDto>(`${this.base}`, dto);
  }

  update(sprintId: number, dto: UpdateSprintDto): Observable<SprintDto> {
    return this.http.put<SprintDto>(`${this.base}${sprintId}`, dto);
  }

  setActive(sprintId: number, isActive: boolean): Observable<any> {
    return this.http.patch(`${this.base}${sprintId}/active?isActive=${isActive}`, null);
  }

  activateExistingAndCarryOver(sprintId: number, carryOverUnfinished: boolean): Observable<any> {
    return this.http.post(`${this.base}${sprintId}/activate?carryOverUnfinished=${carryOverUnfinished}`, null);
  }

  board(sprintId: number): Observable<SprintBoardDto> {
    return this.http.get<SprintBoardDto>(`${this.base}${sprintId}/board`);
  }

  moveTaskToActive(sprintId: number, taskId: number): Observable<any> {
    return this.http.post(`${this.base}${sprintId}/tasks/${taskId}/move-to-active`, null);
  }
}
