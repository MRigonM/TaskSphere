import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { catchError, finalize, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { RepositorySelection } from '../../core/models/github.models';
import { AuthStoreService } from '../../core/services/auth-store.service';
import { GitHubConnectionService } from '../../core/services/github-connection.service';
import { GitHubProjectLinksComponent } from './github-project-links.component';

@Component({
  selector: 'app-github-connection',
  standalone: true,
  imports: [CommonModule, GitHubProjectLinksComponent],
  templateUrl: './github-connection.component.html',
})
export class GitHubConnectionComponent implements OnInit {
  auth = inject(AuthStoreService);
  private github = inject(GitHubConnectionService);

  connection = this.github.connection;

  protected readonly RepositorySelection = RepositorySelection;

  loading = signal(false);
  connecting = signal(false);
  disconnecting = signal(false);
  error = signal<string | null>(null);

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.github
      .loadConnection()
      .pipe(
        catchError(err => {
          // Drop whatever the singleton still holds: without this a failed reload keeps
          // rendering the previously loaded company's account.
          this.github.clear();
          this.error.set(apiErrorMessage(err, 'Failed to load the GitHub connection.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  connect() {
    this.connecting.set(true);
    this.error.set(null);
    this.github
      .getInstallUrl()
      .pipe(
        tap(res => this.github.redirectTo(res.url)),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to start the GitHub install.'));
          this.connecting.set(false); // not reset on success: the browser is navigating to github.com
          return of(null);
        })
      )
      .subscribe();
  }

  disconnect() {
    this.disconnecting.set(true);
    this.error.set(null);
    this.github
      .disconnect()
      .pipe(
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to disconnect GitHub.'));
          return of(null);
        }),
        finalize(() => this.disconnecting.set(false))
      )
      .subscribe();
  }
}
