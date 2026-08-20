import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { SyncActivityResultDto, TaskGitHubActivityDto } from '../models/github-activity.models';

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

  /** Company-wide despite being triggered from a task. Company role only. */
  sync(): Observable<SyncActivityResultDto> {
    return this.http.post<SyncActivityResultDto>(`${environment.apiUrl}GitHub/activity/sync`, null);
  }
}
