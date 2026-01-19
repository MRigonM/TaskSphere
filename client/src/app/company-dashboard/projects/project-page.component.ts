import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';

import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';

import { UserDto, UserQueryDto } from '../../core/models/account.models';
import { AddMemberDto, MemberDto } from '../../core/models/projects.models';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './project-page.component.html',
})
export class ProjectPageComponent {
  projectId = signal<number | null>(null);
  projectName = signal<string>('');
  loading = signal(false);
  error = signal<string | null>(null);

  users = signal<UserDto[]>([]);
  members = signal<MemberDto[]>([]);
  selectedUserId = '';

  constructor(
    private route: ActivatedRoute,
    private projectsApi: ProjectsApiService,
    private accountApi: AccountApiService
  ) {}

  ngOnInit() {
    this.route.paramMap.subscribe((pm) => {
      const raw = pm.get('projectId');
      const id = raw ? Number(raw) : NaN;

      if (!Number.isFinite(id) || id <= 0) {
        this.projectId.set(null);
        this.projectName.set('');
        this.error.set('Invalid project id.');
        return;
      }

      this.projectId.set(id);
      this.error.set(null);

      this.loadProjectName();
      this.loadUsersAndMembers();
    });
  }

  private loadProjectName() {
    const id = this.projectId();
    if (!id) return;

    this.projectsApi.getAll()
      .pipe(
        tap((list) => {
          const p = (list ?? []).find(x => x.id === id);
          this.projectName.set(p?.name ?? `Project #${id}`);
        }),
        catchError(() => {
          this.projectName.set('');
          this.error.set('Failed to load project.');
          return of([]);
        })
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
          this.error.set(this.toMsg(err, 'Failed to load users.'));
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
          this.error.set(this.toMsg(err, 'Failed to load members.'));
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
        tap(() => (this.selectedUserId = '')),
        switchMap(() => this.projectsApi.getMembers(projectId)),
        tap((res) => this.members.set(res ?? [])),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to add member.'));
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
        switchMap(() => this.projectsApi.getMembers(projectId)),
        tap((res) => this.members.set(res ?? [])),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to remove member.'));
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  private toMsg(err: any, fallback: string): string {
    if (Array.isArray(err?.error)) return err.error.join('\n');
    if (typeof err?.error === 'string') return err.error;
    if (err?.status === 0) return 'API unreachable / CORS error.';
    return fallback;
  }
}
