import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { catchError, finalize, switchMap, tap } from 'rxjs/operators';

import { AddMemberDto, MemberDto, ProjectDto } from '../../core/models/projects.models';
import { UserDto, UserQueryDto } from '../../core/models/account.models';
import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';

@Component({
  selector: 'app-project',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './projects.component.html',
})
export class ProjectComponent {
  loading = signal(false);
  error = signal<string | null>(null);

  projects = signal<ProjectDto[]>([]);
  users = signal<UserDto[]>([]);
  members = signal<MemberDto[]>([]);

  name = '';
  selectedUserId = '';
  selectedProject: ProjectDto | null = null;

  constructor(
    private projectsApi: ProjectsApiService,
    private accountApi: AccountApiService
  ) {}

  ngOnInit() {
    this.loadProjects();
  }

  loadProjects() {
    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.getAll()),
        tap((res) => this.projects.set(res ?? [])),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to load projects.'));
          this.projects.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  createProject() {
    const name = this.name.trim();
    if (!name) return;

    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.create({ name })),
        tap((p) => {
          this.name = '';
          if (p) this.projects.set([p, ...this.projects()]);
        }),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to create project.'));
          return of(null);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  openProject(p: ProjectDto) {
    this.selectedProject = p;
    this.selectedUserId = '';
    this.error.set(null);
    this.loadMembersData();
  }

  backToProjects() {
    this.selectedProject = null;
    this.selectedUserId = '';
    this.users.set([]);
    this.members.set([]);
    this.error.set(null);
  }

  loadMembersData() {
    const p = this.selectedProject;
    if (!p) return;

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

  addMember() {
    const p = this.selectedProject;
    if (!p) return;
    if (!this.selectedUserId) return;

    this.loading.set(true);
    this.error.set(null);

    const dto: AddMemberDto = { userId: this.selectedUserId };

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.addMember(p.id, dto)),
        tap(() => {
          this.selectedUserId = '';
        }),
        switchMap(() => this.projectsApi.getMembers(p.id)),
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
    const p = this.selectedProject;
    if (!p) return;

    this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.removeMember(p.id, userId)),
        switchMap(() => this.projectsApi.getMembers(p.id)),
        tap((res) => this.members.set(res ?? [])),
        catchError((err) => {
          this.error.set(this.toMsg(err, 'Failed to remove member.'));
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  private reloadMembers(setLoading: boolean) {
    const p = this.selectedProject;
    if (!p) return;

    if (setLoading) this.loading.set(true);
    this.error.set(null);

    of(null)
      .pipe(
        switchMap(() => this.projectsApi.getMembers(p.id)),
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

  private toMsg(err: any, fallback: string): string {
    if (Array.isArray(err?.error)) return err.error.join('\n');
    if (typeof err?.error === 'string') return err.error;
    if (err?.status === 0) return 'API unreachable / CORS error.';
    return fallback;
  }
}
