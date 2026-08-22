import { CommonModule } from '@angular/common';
import { Component, computed, EventEmitter, inject, Input, OnInit, Output, signal } from '@angular/core';
import { catchError, finalize, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { BranchSuggestionDto, CreatedBranchDto } from '../../core/models/github-activity.models';
import { GitHubActivityService } from '../../core/services/github-activity.service';

/**
 * Creates a GitHub branch for a task. The name is editable because slugging a title like
 * "Fix: user's @mentions // v2" is where the generator is weakest — but it must keep the task
 * key, or the branch will never link to the task the button was pressed on. That check is a
 * hint here and authoritative on the server, which runs the same scanner the sync runs.
 */
@Component({
  selector: 'app-create-branch-dialog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './create-branch-dialog.component.html',
})
export class CreateBranchDialogComponent implements OnInit {
  @Input({ required: true }) taskId!: number;

  @Output() created = new EventEmitter<CreatedBranchDto>();
  @Output() closed = new EventEmitter<void>();

  private activityApi = inject(GitHubActivityService);

  suggestion = signal<BranchSuggestionDto | null>(null);
  loading = signal(false);
  creating = signal(false);
  error = signal<string | null>(null);

  name = signal('');
  repositoryId = signal<number | null>(null);

  /** A hint, not the rule: the server refuses anything this misses. */
  keepsKey = computed(() => {
    const key = this.suggestion()?.taskKey;
    return !!key && this.name().includes(key);
  });

  canCreate = computed(() => !!this.suggestion() && this.keepsKey() && !this.creating());

  ngOnInit() {
    this.loading.set(true);

    this.activityApi
      .suggestBranch(this.taskId)
      .pipe(
        tap(suggestion => {
          this.suggestion.set(suggestion);
          this.name.set(suggestion.suggestedName);
          this.repositoryId.set(suggestion.repositories.length === 1 ? suggestion.repositories[0].id : null);
        }),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Could not work out a branch name for this task.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  onName(value: string) {
    this.name.set(value);
  }

  onRepository(value: string) {
    this.repositoryId.set(value ? Number(value) : null);
  }

  create() {
    if (!this.canCreate()) return;

    this.creating.set(true);
    // Cleared when the run STARTS: the failure path never overwrites it, so a second attempt
    // would otherwise show the first attempt's error under a request still in flight.
    this.error.set(null);

    this.activityApi
      .createBranch(this.taskId, { repositoryId: this.repositoryId(), name: this.name() })
      .pipe(
        tap(branch => {
          this.created.emit(branch);
          this.closed.emit();
        }),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Failed to create the branch.'));
          return of(null);
        }),
        finalize(() => this.creating.set(false))
      )
      .subscribe();
  }
}
