import { CommonModule } from '@angular/common';
import { Component, computed, inject, Input, OnChanges, signal, SimpleChanges } from '@angular/core';
import { catchError, finalize, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import {
  PullRequestState,
  SyncFailureDto,
  TaskGitHubActivityDto,
} from '../../core/models/github-activity.models';
import { AuthStoreService } from '../../core/services/auth-store.service';
import { GitHubActivityService } from '../../core/services/github-activity.service';

/**
 * A task's GitHub activity, read from the mirror. It owns its own load and stays mounted
 * behind `[hidden]` in the modal rather than `*ngIf`, so the count is on the tab before the
 * tab is clicked — one request per modal open, no separate count endpoint.
 *
 * It deliberately does NOT branch on whether the company is connected to GitHub:
 * `GET api/GitHub/connection` sits on the Company-gated controller, so a `User`-role member
 * cannot call it, and a tab that rendered for admins but not members would be worse than one
 * that is occasionally empty.
 */
@Component({
  selector: 'app-task-github-activity',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './task-github-activity.component.html',
})
export class TaskGitHubActivityComponent implements OnChanges {
  @Input({ required: true }) taskId!: number;

  private activityApi = inject(GitHubActivityService);
  private auth = inject(AuthStoreService);

  /** null means unknown — not read yet, or the read failed. Never "nothing is linked". */
  data = signal<TaskGitHubActivityDto | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  syncing = signal(false);
  /** Reported, not thrown: a repository that failed does not make the run a failure. */
  syncFailures = signal<SyncFailureDto[]>([]);

  isCompanyAdmin = this.auth.isCompany();

  readonly PullRequestState = PullRequestState;

  count = computed(() => {
    const d = this.data();
    return d ? d.commits.length + d.branches.length + d.pullRequests.length : 0;
  });

  /** Distinguishes "read answered with nothing" from "no answer yet", which look identical. */
  isEmpty = computed(() => this.data() !== null && this.count() === 0);

  ngOnChanges(changes: SimpleChanges) {
    if (!changes['taskId']?.currentValue) return;

    // Back to unknown before reading a different task. `load()` deliberately leaves `data`
    // alone when a read fails, which is right for a retry of the same task and wrong across
    // a task change — without this, task 42's commits stay on screen under an error about 43.
    this.data.set(null);
    this.load();
  }

  retry() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);

    this.activityApi
      .getForTask(this.taskId)
      .pipe(
        tap(data => this.data.set(data)),
        catchError(err => {
          // data is left untouched, so the template renders the error rather than the empty
          // state over a request that never answered.
          this.error.set(apiErrorMessage(err, 'Failed to load the GitHub activity.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  sync() {
    this.syncing.set(true);
    this.error.set(null);
    this.syncFailures.set([]);

    this.activityApi
      .sync()
      .pipe(
        tap(result => {
          this.syncFailures.set(result.failures);
          this.load();
        }),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to sync GitHub activity.'));
          return of(null);
        }),
        finalize(() => this.syncing.set(false))
      )
      .subscribe();
  }

  /** The subject line. A commit body belongs on GitHub, not in a modal. */
  subject(message: string): string {
    return message.split('\n')[0];
  }

  stateLabel(state: PullRequestState): string {
    if (state === PullRequestState.Merged) return 'Merged';
    return state === PullRequestState.Closed ? 'Closed' : 'Open';
  }
}
