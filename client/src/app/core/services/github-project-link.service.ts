import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ProjectRepositoriesDto, ProjectRepositoryLinkDto } from '../models/github.models';

/**
 * The three per-project link endpoints. `repositoryId` throughout is the local
 * `GitHubRepository.Id` (`GitHubRepositoryDto.id`), not the GitHub-issued repository id.
 */
@Injectable({ providedIn: 'root' })
export class GitHubProjectLinkService {
  private base = `${environment.apiUrl}GitHub/projects`;
  private http = inject(HttpClient);

  getProjectRepositories(projectId: number): Observable<ProjectRepositoriesDto> {
    return this.http.get<ProjectRepositoriesDto>(`${this.base}/${projectId}/repositories`);
  }

  link(projectId: number, repositoryId: number): Observable<ProjectRepositoryLinkDto> {
    return this.http.post<ProjectRepositoryLinkDto>(`${this.base}/${projectId}/repositories`, {
      repositoryId,
    });
  }

  /** The API answers `Ok()` with no body. */
  unlink(projectId: number, repositoryId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${projectId}/repositories/${repositoryId}`);
  }
}
