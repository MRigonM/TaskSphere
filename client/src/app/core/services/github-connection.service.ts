import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  ConnectGitHubDto,
  GitHubConnectionDto,
  GitHubInstallUrlDto,
} from '../models/github.models';

@Injectable({ providedIn: 'root' })
export class GitHubConnectionService {
  private base = `${environment.apiUrl}GitHub`;
  private http = inject(HttpClient);

  private _connection = signal<GitHubConnectionDto | null>(null);

  readonly connection = this._connection.asReadonly();

  clear() {
    this._connection.set(null);
  }

  loadConnection(): Observable<GitHubConnectionDto> {
    return this.http
      .get<GitHubConnectionDto>(`${this.base}/connection`)
      .pipe(tap(c => this._connection.set(c)));
  }

  /**
   * The API answers with `Ok()` and no body. Its outcome is deterministic — the installation and
   * its repositories are soft-deleted, so a reload would return exactly the "not connected" shape
   * below. Set it locally instead: a second round-trip could fail on its own and leave the screen
   * in the *unknown* state (null connection) even though the disconnect succeeded.
   */
  disconnect(): Observable<void> {
    return this.http
      .delete<void>(`${this.base}/connection`)
      .pipe(tap(() => this._connection.set({ installation: null, repositories: [] })));
  }

  getInstallUrl(): Observable<GitHubInstallUrlDto> {
    return this.http.get<GitHubInstallUrlDto>(`${this.base}/install`);
  }

  completeInstall(dto: ConnectGitHubDto): Observable<GitHubConnectionDto> {
    return this.http
      .post<GitHubConnectionDto>(`${this.base}/callback`, dto)
      .pipe(tap(c => this._connection.set(c)));
  }

  /**
   * Re-reads the installation's repositories from GitHub. Sends no body — the server resolves
   * the installation from the authenticated company, so there is nothing to name.
   * On failure the stored connection is deliberately left as it was: the repositories that were
   * already loaded are still real, and blanking them would turn a refresh failure into an
   * apparent loss of data.
   */
  refreshRepositories(): Observable<GitHubConnectionDto> {
    return this.http
      .post<GitHubConnectionDto>(`${this.base}/repositories/sync`, {})
      .pipe(tap(c => this._connection.set(c)));
  }

  /** Seam for the top-level navigation to github.com, so tests can assert it without navigating. */
  redirectTo(url: string) {
    window.location.href = url;
  }
}
