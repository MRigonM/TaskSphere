import { Component, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {FormBuilder, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import { catchError, finalize, of, switchMap, tap } from 'rxjs';
import { TasksApiService } from '../core/services/tasks-api.service';
import { SprintsApiService } from '../core/services/sprints-api.service';
import { CreateTaskDto, TaskDto, UpdateTaskDto } from '../core/models/tasks.models';
import { SprintDto } from '../core/models/sprints.models';
import {CommonModule} from '@angular/common';
import { AccountApiService } from '../core/services/account-api.service';
import { UserDto, UserQueryDto } from '../core/models/account.models';


@Component({
  selector: 'app-tasks-page',
  templateUrl: './tasks-page.component.html',
  imports: [
    CommonModule,
    ReactiveFormsModule
  ]
})
export class TasksPageComponent {
  loading = signal(false);
  error = signal<string | null>(null);
  users = signal<UserDto[]>([]);
  projectId = signal<number>(0);

  sprints = signal<SprintDto[]>([]);
  activeSprint = signal<SprintDto | null>(null);

  backlog = signal<TaskDto[]>([]);
  sprintTasks = signal<TaskDto[]>([]);

  showCreate = signal(false);
  editing = signal<TaskDto | null>(null);

  createForm: FormGroup;
  editForm: FormGroup;

  statuses = ['Open', 'InProgress', 'Blocked', 'Done'];

  constructor(
    private route: ActivatedRoute,
    private fb: FormBuilder,
    private tasksApi: TasksApiService,
    private sprintsApi: SprintsApiService,
    private accountApi: AccountApiService
  ) {
    this.createForm = this.fb.group({
      title: ['', [Validators.required]],
      description: [''],
      status: ['Open', [Validators.required]],
      priority: [''],
      storyPoints: [''],
      sprintId: [''],
      assigneeUserId: [''],
    });

    this.editForm = this.fb.group({
      title: ['', [Validators.required]],
      description: [''],
      status: ['Open', [Validators.required]],
      priority: [''],
      storyPoints: [''],
      sprintId: [''],
      assigneeUserId: [''],
    });
  }

  ngOnInit() {
    const pid = Number(this.route.snapshot.paramMap.get('projectId') ?? 0);
    this.projectId.set(pid);
    this.reloadAll();
  }

  reloadAll() {
    const pid = this.projectId();
    if (!pid) return;

    this.loading.set(true);
    this.error.set(null);

    const query: UserQueryDto = { page: 1, pageSize: 100 };

    of(null).pipe(
      switchMap(() => this.sprintsApi.getByProject(pid)),
      tap((list) => {
        const arr = list ?? [];
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);

        const active = arr.find(x => x.isActive) ?? null;
        this.activeSprint.set(active);

        if (active) this.createForm.patchValue({ sprintId: String(active.id) });
      }),

      switchMap(() => this.accountApi.getUsers(query)),
      tap((res) => {
        const list = (res ?? []).filter(u => !u.isDeleted);
        this.users.set(list);
      }),

      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),

      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),

      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to load tasks.'));
        this.sprints.set([]);
        this.users.set([])
        this.backlog.set([]);
        this.sprintTasks.set([]);
        this.activeSprint.set(null);
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  selectSprint(s: SprintDto | null) {
    this.activeSprint.set(s);
    this.loadSprintTasks();
  }

  assigneeName(userId: string | null | undefined): string {
    if (!userId) return 'Unassigned';
    const u = this.users().find(x => x.id === userId);
    return u ? u.name : 'Unassigned';
  }


  assignTask(t: TaskDto, assigneeUserId: string) {
    const pid = this.projectId();
    const value = assigneeUserId ? assigneeUserId : null;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.assign(t.id, value)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to assign task.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  loadSprintTasks() {
    const s = this.activeSprint();
    if (!s) {
      this.sprintTasks.set([]);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.getBySprint(s.id)),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to load sprint tasks.'));
        this.sprintTasks.set([]);
        return of([]);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  loadBacklog() {
    const pid = this.projectId();
    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to load backlog.'));
        this.backlog.set([]);
        return of([]);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  openCreate() {
    this.showCreate.set(true);
    this.editing.set(null);
    this.error.set(null);

    const s = this.activeSprint();
    this.createForm.reset({
      title: '',
      description: '',
      status: 'Open',
      priority: '',
      storyPoints: '',
      sprintId: s ? String(s.id) : '',
      assigneeUserId: '',
    });
  }

  cancelCreate() {
    this.showCreate.set(false);
  }

  submitCreate() {
    if (this.createForm.invalid) return;

    const pid = this.projectId();
    const v = this.createForm.value;

    const sprintId = v.sprintId ? Number(v.sprintId) : null;

    const dto: CreateTaskDto = {
      title: (v.title ?? '').trim(),
      description: (v.description ?? '') || null,
      status: v.status ?? 'Open',
      priority: (v.priority ?? '') || null,
      storyPoints: this.toNullableNumber(v.storyPoints),
      projectId: pid,
      sprintId: sprintId,
      assigneeUserId: (v.assigneeUserId ?? '') || null,
    };

    if (!dto.title) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.create(dto)),
      tap(() => this.showCreate.set(false)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to create task.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  startEdit(t: TaskDto) {
    this.editing.set(t);
    this.showCreate.set(false);
    this.error.set(null);

    this.editForm.reset({
      title: t.title ?? '',
      description: t.description ?? '',
      status: t.status ?? 'Open',
      priority: t.priority ?? '',
      storyPoints: t.storyPoints ?? '',
      sprintId: t.sprintId ? String(t.sprintId) : '',
      assigneeUserId: t.assigneeUserId ?? '',
    });
  }

  cancelEdit() {
    this.editing.set(null);
  }

  submitEdit() {
    const t = this.editing();
    if (!t) return;
    if (this.editForm.invalid) return;

    const v = this.editForm.value;
    const pid = this.projectId();

    const dto: UpdateTaskDto = {
      title: (v.title ?? '').trim(),
      description: (v.description ?? '') || null,
      status: v.status ?? 'Open',
      priority: (v.priority ?? '') || null,
      storyPoints: this.toNullableNumber(v.storyPoints),
      sprintId: v.sprintId ? Number(v.sprintId) : null,
      assigneeUserId: (v.assigneeUserId ?? '') || null,
    };

    if (!dto.title) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.update(t.id, dto)),
      tap(() => this.editing.set(null)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to update task.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  deleteTask(t: TaskDto) {
    const pid = this.projectId();

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.delete(t.id)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to delete task.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  moveToBacklog(t: TaskDto) {
    const pid = this.projectId();
    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.moveToBacklog(t.id)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to move task to backlog.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  moveToSprint(t: TaskDto) {
    const s = this.activeSprint();
    if (!s) return;

    const pid = this.projectId();
    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.moveToSprint(t.id, s.id)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => this.tasksApi.getBySprint(s.id)),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to move task to sprint.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  setStatus(t: TaskDto, status: string) {
    const pid = this.projectId();
    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.setStatus(t.id, status)),
      switchMap(() => this.tasksApi.getBacklog(pid)),
      tap((tasks) => this.backlog.set(tasks ?? [])),
      switchMap(() => {
        const s = this.activeSprint();
        return s ? this.tasksApi.getBySprint(s.id) : of([]);
      }),
      tap((tasks) => this.sprintTasks.set(tasks ?? [])),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to set status.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  private toNullableNumber(v: any): number | null {
    if (v === null || v === undefined || v === '') return null;
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }

  private toMsg(err: any, fallback: string): string {
    if (Array.isArray(err?.error)) return err.error.join('\n');
    if (typeof err?.error === 'string') return err.error;
    if (err?.status === 0) return 'API unreachable / CORS error.';
    return fallback;
  }
}
