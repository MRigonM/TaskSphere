import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';

import { apiErrorMessage } from '../../core/http/api-error';
import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';
import { AuthStoreService } from '../../core/services/auth-store.service';
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

  /** null means unknown — not read yet, or the read failed. Never "nothing is linked". */
  repositories = signal<ProjectRepositoriesDto | null>(null);
  repositoriesError = signal<string | null>(null);

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
    });
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
