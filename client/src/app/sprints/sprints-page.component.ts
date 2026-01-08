import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {FormBuilder, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import { finalize, of, switchMap, tap, catchError } from 'rxjs';
import { signal } from '@angular/core';

import { SprintsApiService } from '../core/services/sprints-api.service';
import { CreateSprintDto, SprintBoardDto, SprintDto, UpdateSprintDto } from '../core/models/sprints.models';
import {CommonModule, DatePipe} from '@angular/common';
import {UserDto, UserQueryDto} from '../core/models/account.models';
import {AccountApiService} from '../core/services/account-api.service';
import {TasksApiService} from '../core/services/tasks-api.service';

@Component({
  selector: 'app-sprints-page',
  templateUrl: './sprints-page.component.html',
  imports: [
    CommonModule,
    DatePipe,
    ReactiveFormsModule
  ]
})
export class SprintsPageComponent {
  loading = signal(false);
  error = signal<string | null>(null);
  users = signal<UserDto[]>([]);
  statuses = ['Open', 'InProgress', 'Blocked', 'Done'];
  projectId = signal<number>(0);

  sprints = signal<SprintDto[]>([]);
  selectedSprint = signal<SprintDto | null>(null);
  board = signal<SprintBoardDto | null>(null);

  showCreate = signal(false);
  showEdit = signal(false);

  createForm: FormGroup;
  editForm: FormGroup;

  carryOverUnfinished = signal(true);

  constructor(
    private route: ActivatedRoute,
    private fb: FormBuilder,
    private api: SprintsApiService,
    private accountApi: AccountApiService,
    private tasksApi: TasksApiService
  ) {
    this.createForm = this.fb.group({
      name: ['', [Validators.required]],
      startDate: ['', [Validators.required]],
      endDate: ['', [Validators.required]],
      isActive: [true, [Validators.required]],
    });

    this.editForm = this.fb.group({
      name: ['', [Validators.required]],
      startDate: ['', [Validators.required]],
      endDate: ['', [Validators.required]],
    });
  }

  ngOnInit() {
    const pid = Number(this.route.snapshot.paramMap.get('projectId') ?? 0);
    this.projectId.set(pid);
    this.loadUsers();
    this.loadSprints(true);
  }

  loadSprints(selectActiveIfPossible: boolean) {
    const pid = this.projectId();
    if (!pid) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.getByProject(pid)),
      tap((list) => {
        const s = list ?? [];
        s.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(s);

        if (!selectActiveIfPossible) return;

        const active = s.find(x => x.isActive);
        if (active) this.selectSprint(active);
        else if (s.length) this.selectSprint(s[0]);
        else {
          this.selectedSprint.set(null);
          this.board.set(null);
        }
      }),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to load sprints.'));
        this.sprints.set([]);
        this.selectedSprint.set(null);
        this.board.set(null);
        return of([]);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  loadUsers() {
    const query: UserQueryDto = { page: 1, pageSize: 100 };

    of(null).pipe(
      switchMap(() => this.accountApi.getUsers(query)),
      tap((res) => this.users.set((res ?? []).filter(u => !u.isDeleted))),
      catchError(() => {
        this.users.set([]);
        return of([]);
      })
    ).subscribe();
  }

  assigneeName(userId: string | null | undefined): string {
    if (!userId) return 'Unassigned';
    const u = this.users().find(x => x.id === userId);
    return u ? u.name : 'Unassigned';
  }

  taskStatus(t: any): string {
    return (t?.status ?? 'Open').toString();
  }

  selectSprint(s: SprintDto) {
    this.selectedSprint.set(s);
    this.showEdit.set(false);
    this.showCreate.set(false);
    this.loadBoard();
  }

  setTaskStatusFromBoard(t: any, status: string) {
    const s = this.selectedSprint();
    if (!s) return;

    const id = this.taskId(t);
    if (!id) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.setStatus(id, status)),
      switchMap(() => this.api.board(s.id)),
      tap((b) => this.board.set(b)),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to set task status.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  assignFromBoard(t: any, assigneeUserId: string) {
    const s = this.selectedSprint();
    if (!s) return;

    const id = this.taskId(t);
    if (!id) return;
    tap(() => this.loadBoard());

    const value = assigneeUserId ? assigneeUserId : null;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.assign(id, value)),
      switchMap(() => this.api.board(s.id)),
      tap((b) => this.board.set(b)),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to assign task.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  loadBoard() {
    const s = this.selectedSprint();
    if (!s) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.board(s.id)),
      tap((b) => this.board.set(b)),

      switchMap(() => this.tasksApi.getBySprint(s.id)),
      tap((tasks) => {
        const b = this.board();
        if (!b) return;

        const assigneeMap = new Map<number, string | null>();
        (tasks ?? []).forEach(t => assigneeMap.set(t.id, t.assigneeUserId ?? null));

        const merge = (arr: any[]) =>
          (arr ?? []).map(x => ({
            ...x,
            assigneeUserId: assigneeMap.get(x.id) ?? x.assigneeUserId ?? null,
          }));

        this.board.set({
          ...b,
          open: merge(b.open),
          inProgress: merge(b.inProgress),
          blocked: merge(b.blocked),
          done: merge(b.done),
        });
      }),

      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to load sprint board.'));
        this.board.set(null);
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }


  openCreate() {
    this.showCreate.set(true);
    this.showEdit.set(false);
    this.error.set(null);

    this.createForm.reset({
      name: '',
      startDate: '',
      endDate: '',
      isActive: true,
    });
  }

  cancelCreate() {
    this.showCreate.set(false);
    this.error.set(null);
  }

  submitCreate() {
    if (this.createForm.invalid) return;

    const pid = this.projectId();
    const v = this.createForm.value;

    const dto: CreateSprintDto = {
      name: (v.name ?? '').trim(),
      startDate: new Date(v.startDate).toISOString(),
      endDate: new Date(v.endDate).toISOString(),
      isActive: !!v.isActive,
      projectId: pid,
    };

    if (!dto.name) return;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.create(dto)),
      tap((created) => {
        this.showCreate.set(false);
      }),
      switchMap(() => this.api.getByProject(pid)),
      tap((list) => {
        const s = list ?? [];
        s.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(s);

        const active = s.find(x => x.isActive);
        if (active) this.selectSprint(active);
        else if (s.length) this.selectSprint(s[0]);
      }),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to create sprint.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  openEdit() {
    const s = this.selectedSprint();
    if (!s) return;

    this.showEdit.set(true);
    this.showCreate.set(false);
    this.error.set(null);

    this.editForm.reset({
      name: s.name ?? '',
      startDate: this.toDateInputValue(s.startDate),
      endDate: this.toDateInputValue(s.endDate),
    });
  }

  cancelEdit() {
    this.showEdit.set(false);
    this.error.set(null);
  }

  submitEdit() {
    const s = this.selectedSprint();
    if (!s) return;
    if (this.editForm.invalid) return;

    const v = this.editForm.value;

    const dto: UpdateSprintDto = {
      name: (v.name ?? '').trim(),
      startDate: new Date(v.startDate).toISOString(),
      endDate: new Date(v.endDate).toISOString(),
    };

    if (!dto.name) return;

    const pid = this.projectId();

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.update(s.id, dto)),
      switchMap(() => this.api.getByProject(pid)),
      tap((list) => {
        const arr = list ?? [];
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);

        const updated = arr.find(x => x.id === s.id) ?? null;
        if (updated) this.selectedSprint.set(updated);

        this.showEdit.set(false);
      }),
      switchMap(() => {
        const sel = this.selectedSprint();
        return sel ? this.api.board(sel.id) : of(null);
      }),
      tap((b) => this.board.set(b)),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to update sprint.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  setActiveFlag(isActive: boolean) {
    const s = this.selectedSprint();
    if (!s) return;

    const pid = this.projectId();

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.setActive(s.id, isActive)),
      switchMap(() => this.api.getByProject(pid)),
      tap((list) => {
        const arr = list ?? [];
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);

        const updated = arr.find(x => x.id === s.id) ?? null;
        if (updated) this.selectedSprint.set(updated);
      }),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to set active flag.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  activateSprintAndCarryOver() {
    const s = this.selectedSprint();
    if (!s) return;

    const pid = this.projectId();
    const carry = this.carryOverUnfinished();

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.activateExistingAndCarryOver(s.id, carry)),
      switchMap(() => this.api.getByProject(pid)),
      tap((list) => {
        const arr = list ?? [];
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);

        const updated = arr.find(x => x.id === s.id) ?? null;
        if (updated) this.selectedSprint.set(updated);
      }),
      switchMap(() => this.api.board(s.id)),
      tap((b) => this.board.set(b)),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to activate sprint.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  taskId(t: any): number {
    return Number(t?.id ?? 0);
  }

  taskTitle(t: any): string {
    return (t?.title ?? t?.name ?? `Task #${t?.id ?? ''}`).toString();
  }

  private toDateInputValue(iso: string | null | undefined): string {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  private toMsg(err: any, fallback: string): string {
    if (Array.isArray(err?.error)) return err.error.join('\n');
    if (typeof err?.error === 'string') return err.error;
    if (err?.status === 0) return 'API unreachable / CORS error.';
    return fallback;
  }
}
