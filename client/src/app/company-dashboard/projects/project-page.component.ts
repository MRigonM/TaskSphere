import { CommonModule } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';

import { GitHubReturnSyncService } from '../../core/github/github-return-sync.service';
import { manageInstallationUrl } from '../../core/github/manage-installation-url';
import { apiErrorMessage } from '../../core/http/api-error';
import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';
import { AuthStoreService } from '../../core/services/auth-store.service';
import { GitHubConnectionService } from '../../core/services/github-connection.service';
import { GitHubProjectLinkService } from '../../core/services/github-project-link.service';
import { ToastService } from '../../core/services/toast.service';

import { UserDto, UserQueryDto } from '../../core/models/account.models';
import { ProjectRepositoriesDto } from '../../core/models/github.models';
import { AddMemberDto, MemberDto, ProjectDto } from '../../core/models/projects.models';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './project-page.component.html',
})
export class ProjectPageComponent {
  projectId = signal<number | null>(null);
  projectName = signal<string>('');
  /** The whole project, not just its name: the settings toggle renders from it. */
  project = signal<ProjectDto | null>(null);
  savingSettings = signal(false);
  loading = signal(false);
  error = signal<string | null>(null);

  users = signal<UserDto[]>([]);
  members = signal<MemberDto[]>([]);
  selectedUserId = '';

  auth = inject(AuthStoreService);
  private linkService = inject(GitHubProjectLinkService);
  private github = inject(GitHubConnectionService);
  private returnSync = inject(GitHubReturnSyncService);
  private destroyRef = inject(DestroyRef);

  /** null means unknown — not read yet, or the read failed. Never "nothing is linked". */
  repositories = signal<ProjectRepositoriesDto | null>(null);
  repositoriesError = signal<string | null>(null);

  connection = this.github.connection;
  linking = signal(false);

  /** Repositories the installation can see that this project is not linked to yet. */
  linkable = computed(() => {
    const linked = new Set((this.repositories()?.links ?? []).map(l => l.gitHubRepositoryId));
    return (this.connection()?.repositories ?? []).filter(r => !linked.has(r.id));
  });

  /** null when there is nothing to do on GitHub — not connected, or access is already "all". */
  manageUrl = computed(() => manageInstallationUrl(this.connection()?.installation ?? null));

  constructor(
    private route: ActivatedRoute,
    private projectsApi: ProjectsApiService,
    private accountApi: AccountApiService,
    private toast: ToastService,
  ) {}

  ngOnInit() {
    this.route.paramMap.subscribe((pm) => {
      const raw = pm.get('projectId');
      const id = raw ? Number(raw) : NaN;

      if (!Number.isFinite(id) || id <= 0) {
        this.projectId.set(null);
        this.projectName.set('');
        this.project.set(null);
        this.error.set('Invalid project id.');
        return;
      }

      this.projectId.set(id);
      this.error.set(null);

      this.loadProjectName();
      this.loadUsersAndMembers();
      this.loadRepositories();
      this.loadConnectionIfAdmin();
    });

    // Outside the paramMap callback on purpose: paramMap emits again on an in-place navigation,
    // and one subscription per emission would multiply the sync. takeUntilDestroyed is not a
    // precaution either — returned$ never completes, so a destroyed page would keep syncing.
    this.returnSync.returned$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.github
        .refreshRepositories()
        .pipe(
          tap(() => this.loadRepositories()),
          catchError(err => {
            this.repositoriesError.set(apiErrorMessage(err, 'Failed to refresh the repositories.'));
            return of(null);
          }),
        )
        .subscribe();
    });
  }

  /**
   * Opens the installation's own settings in a new tab. A new tab rather than a navigation: the
   * return is detected by this document regaining visibility, which only works if it survives.
   */
  openGitHubSettings() {
    const url = this.manageUrl();
    if (!url) return;

    this.returnSync.arm();
    window.open(url, '_blank');
  }

  loadRepositories() {
    const id = this.projectId();
    if (id === null) return;

    this.linkService
      .getProjectRepositories(id)
      .pipe(
        tap(data => this.repositories.set(data)),
        catchError(err => {
          this.repositoriesError.set(apiErrorMessage(err, 'Failed to load the repositories.'));
          return of(null);
        }),
      )
      .subscribe();
  }

  /**
   * Admin-only: GET GitHub/connection is Company-gated, so calling it as a member is a
   * guaranteed 403 and an error message about something they cannot act on.
   */
  loadConnectionIfAdmin() {
    if (!this.auth.isCompany()) return;

    this.github
      .loadConnection()
      .pipe(
        catchError(err => {
          this.repositoriesError.set(apiErrorMessage(err, 'Failed to load the GitHub connection.'));
          return of(null);
        }),
      )
      .subscribe();
  }

  linkRepository(repositoryId: number) {
    const id = this.projectId();
    if (id === null) return;
    // The picker's placeholder has value "", which the template coerces to 0. Choosing it is not
    // a request to link anything, and posting it could only ever fail.
    if (!repositoryId) return;

    this.linking.set(true);
    this.repositoriesError.set(null);

    this.linkService
      .link(id, repositoryId)
      .pipe(
        // Refetched, not patched: unavailableCount is derivable only server-side.
        tap(() => this.loadRepositories()),
        catchError(err => {
          this.repositoriesError.set(apiErrorMessage(err, 'Failed to link the repository.'));
          return of(null);
        }),
        finalize(() => this.linking.set(false)),
      )
      .subscribe();
  }

  unlinkRepository(repositoryId: number) {
    const id = this.projectId();
    if (id === null) return;

    this.linking.set(true);
    this.repositoriesError.set(null);

    this.linkService
      .unlink(id, repositoryId)
      .pipe(
        tap(() => this.loadRepositories()),
        catchError(err => {
          this.repositoriesError.set(apiErrorMessage(err, 'Failed to unlink the repository.'));
          return of(null);
        }),
        finalize(() => this.linking.set(false)),
      )
      .subscribe();
  }

  private loadProjectName() {
    const id = this.projectId();
    if (!id) return;

    this.projectsApi.getAll()
      .pipe(
        tap((list) => {
          const p = (list ?? []).find(x => x.id === id);
          this.project.set(p ?? null);
          this.projectName.set(p?.name ?? `Project #${id}`);
        }),
        catchError(() => {
          this.projectName.set('');
          this.project.set(null);
          this.error.set('Failed to load project.');
          return of([]);
        })
      )
      .subscribe();
  }


  onAutoDoneOnMergeChanged(event: Event) {
    const projectId = this.projectId();
    if (!projectId) return;

    const input = event.target as HTMLInputElement;
    const enabled = input.checked;

    this.savingSettings.set(true);
    this.error.set(null);

    this.projectsApi.updateSettings(projectId, enabled)
      .pipe(
        tap((updated) => {
          this.project.set(updated);
          this.toast.show(
            enabled
              ? 'Merged pull requests will move their task to Done'
              : 'Merged pull requests will no longer move their task',
            'info',
          );
        }),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to update project settings.'));
          // The browser has already flipped the box. The bound signal still holds the old
          // value, so Angular sees no change and will not put it back — the screen would
          // otherwise claim a setting that was never saved.
          input.checked = !enabled;
          return of(null);
        }),
        finalize(() => this.savingSettings.set(false)),
      )
      .subscribe();
  }
  loadUsersAndMembers() {
    const projectId = this.projectId();
    if (!projectId) return;

    this.loading.set(true);
    this.error.set(null);

    const query: UserQueryDto = { page: 1, pageSize: 100 };

    of(null)
      .pipe(
        switchMap(() => this.accountApi.getUsers(query)),
        tap((res) => {
          const list = (res ?? []).filter(u => !u.isDeleted);
          this.users.set(list);
        }),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to load users.'));
          this.users.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();

    this.reloadMembers(true);
  }

  reloadMembers(setLoading: boolean) {
    const projectId = this.projectId();
    if (!projectId) return;

    if (setLoading) this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.getMembers(projectId)),
        tap((res) => this.members.set(res ?? [])),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to load members.'));
          this.members.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  addMember() {
    const projectId = this.projectId();
    if (!projectId) return;
    if (!this.selectedUserId) return;

    this.loading.set(true);
    this.error.set(null);

    const dto: AddMemberDto = { userId: this.selectedUserId };

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.addMember(projectId, dto)),
        tap(() => { this.selectedUserId = ''; this.toast.show('Member was added'); }),
        switchMap(() => this.projectsApi.getMembers(projectId)),
        tap((res) => this.members.set(res ?? [])),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to add member.'));
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  removeMember(userId: string) {
    const projectId = this.projectId();
    if (!projectId) return;

    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.removeMember(projectId, userId)),
        tap(() => this.toast.show('Member was removed', 'info')),
        switchMap(() => this.projectsApi.getMembers(projectId)),
        tap((res) => this.members.set(res ?? [])),
        catchError((err) => {
          this.error.set(apiErrorMessage(err, 'Failed to remove member.'));
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }
}
