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
}
