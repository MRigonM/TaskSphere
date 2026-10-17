import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { GitHubConnectionService } from '../../core/services/github-connection.service';

@Component({
  selector: 'app-github-callback',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './github-callback.component.html',
})
export class GitHubCallbackComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private github = inject(GitHubConnectionService);

  error = signal<string | null>(null);

  ngOnInit() {
    const params = this.route.snapshot.queryParamMap;
    const installationId = Number(params.get('installation_id'));
    const state = params.get('state');
    const code = params.get('code');

    // GitHub sends the user here after an installation UPDATE — repository access changed on an
    // App that has "Redirect on update" enabled. That trip carries no `state`, because it was
    // never our install flow, so judging it by the rules below reports a failed authorization
    // for something that succeeded. Nothing is established here: the sync endpoint resolves the
    // installation from the authenticated company, so nothing in this URL is read or trusted.
    if (params.get('setup_action') === 'update') {
      this.refreshAfterUpdate();
      return;
    }

    // The same URL is registered as both the App's Setup URL and its Callback URL (§0k). Only
    // the authorization redirect carries `code`; a bare setup redirect (`setup_action` alone)
    // cannot be exchanged, so it falls through to the recovery message and the Connect button
    // on the connection screen (§0q).
    if (!installationId || !state || !code) {
      this.error.set(
        'The GitHub connection could not be completed — the authorization was not finished.'
      );
      return;
    }

    this.github
      .completeInstall({ installationId, state, code })
      .pipe(
        tap(() => this.router.navigateByUrl('/dashboard/github')),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'The GitHub connection could not be completed.'));
          return of(null);
        })
      )
      .subscribe();
  }

  /**
   * The repository list is what an update changes, and it is only re-read on demand — so without
   * this the user lands back on a screen that still cannot show the repository they just granted.
   */
  private refreshAfterUpdate() {
    this.github
      .refreshRepositories()
      .pipe(
        tap(() => this.router.navigateByUrl('/dashboard/github')),
        catchError(err => {
          this.error.set(
            apiErrorMessage(err, 'The repositories could not be refreshed after the update.')
          );
          return of(null);
        })
      )
      .subscribe();
  }
}
