import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLogDto, AuditQueryDto, AuditStatsDto, PagedResult } from '../models/audit.models';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private base = `${environment.apiUrl}Audit`;

  constructor(private http: HttpClient) {}

  getPaged(query: AuditQueryDto): Observable<PagedResult<AuditLogDto>> {
    let params = new HttpParams()
      .set('page', query.page.toString())
      .set('pageSize', query.pageSize.toString());
    if (query.username) params = params.set('username', query.username);
    if (query.action) params = params.set('action', query.action);
    if (query.httpMethod) params = params.set('httpMethod', query.httpMethod);
    return this.http.get<PagedResult<AuditLogDto>>(this.base, { params });
  }

  getStats(days: number): Observable<AuditStatsDto> {
    const params = new HttpParams().set('days', days.toString());
    return this.http.get<AuditStatsDto>(`${this.base}/stats`, { params });
  }
}
