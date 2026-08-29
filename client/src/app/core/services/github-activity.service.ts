import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { SyncActivityResultDto, TaskActivityRefreshDto, TaskGitHubActivityDto, BranchSuggestionDto, CreateBranchDto, CreatedBranchDto } from '../models/github-activity.models';

/**
 * Two endpoints on two controllers, and the split is deliberate: the read sits on
 * `TasksController` (CompanyOrUser) because a project Member must be able to call it, and the
 * sync sits on `GitHubController` (Company only) because it spends installation rate limit.
 */
@Injectable({ providedIn: 'root' })
export class GitHubActivityService {
  private http = inject(HttpClient);

  getForTask(taskId: number): Observable<TaskGitHubActivityDto> {
    return this.http.get<TaskGitHubActivityDto>(
      `${environment.apiUrl}Tasks/${taskId}/github-activity`
    );
  }

  /**
   * Task-scoped and member-reachable, unlike sync(). Fired on open so the panel shows current
   * activity without anyone pressing the admin-only Sync button.
   */
  refreshForTask(taskId: number): Observable<TaskActivityRefreshDto> {
    return this.http.post<TaskActivityRefreshDto>(
      `${environment.apiUrl}Tasks/${taskId}/github-refresh`, null
    );
  }

  /** Company-wide despite being triggered from a task. Company role only. */
  sync(): Observable<SyncActivityResultDto> {
    return this.http.post<SyncActivityResultDto>(`${environment.apiUrl}GitHub/activity/sync`, null);
  }

  /** Both branch endpoints sit on `TasksController`, so a project Member can reach them. */
  suggestBranch(taskId: number): Observable<BranchSuggestionDto> {
    return this.http.get<BranchSuggestionDto>(
      `${environment.apiUrl}Tasks/${taskId}/github-branch/suggestion`
    );
  }

  createBranch(taskId: number, dto: CreateBranchDto): Observable<CreatedBranchDto> {
    return this.http.post<CreatedBranchDto>(`${environment.apiUrl}Tasks/${taskId}/github-branch`, dto);
  }
}
