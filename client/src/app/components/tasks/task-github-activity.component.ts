import { CommonModule } from '@angular/common';
import { Component, computed, inject, Input, OnChanges, signal, SimpleChanges } from '@angular/core';
import { catchError, finalize, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { PullRequestState, TaskGitHubActivityDto } from '../../core/models/github-activity.models';
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

  /** null means unknown — not read yet, or the read failed. Never "nothing is linked". */
  data = signal<TaskGitHubActivityDto | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  readonly PullRequestState = PullRequestState;

  count = computed(() => {
    const d = this.data();
    return d ? d.commits.length + d.branches.length + d.pullRequests.length : 0;
  });

  /** Distinguishes "read answered with nothing" from "no answer yet", which look identical. */
  isEmpty = computed(() => this.data() !== null && this.count() === 0);

  ngOnChanges(changes: SimpleChanges) {
    if (changes['taskId']?.currentValue) this.load();
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

  /** The subject line. A commit body belongs on GitHub, not in a modal. */
  subject(message: string): string {
    return message.split('\n')[0];
  }

  stateLabel(state: PullRequestState): string {
    if (state === PullRequestState.Merged) return 'Merged';
    return state === PullRequestState.Closed ? 'Closed' : 'Open';
  }
}
