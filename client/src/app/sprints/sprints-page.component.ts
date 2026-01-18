import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {FormBuilder, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import { finalize, of, switchMap, tap, catchError, map } from 'rxjs';
import { signal } from '@angular/core';

import { SprintsApiService } from '../core/services/sprints-api.service';
import { CreateSprintDto, SprintBoardDto, SprintDto, UpdateSprintDto } from '../core/models/sprints.models';
import {CommonModule, DatePipe} from '@angular/common';
import {UserDto, UserQueryDto} from '../core/models/account.models';
import {AccountApiService} from '../core/services/account-api.service';
import {TasksApiService} from '../core/services/tasks-api.service';
import {BoardColumnComponent} from '../components/sprints/board-column.component';
import {CdkDragDrop, moveItemInArray, transferArrayItem} from '@angular/cdk/drag-drop';
import {AuthStoreService} from '../core/services/auth-store.service';
import {ProjectsApiService} from '../company-dashboard/projects/projects.service';
import {MemberDto} from '../core/models/projects.models';
import {TaskDetailsModalComponent} from '../components/tasks/task-details-modal.component';

@Component({
  selector: 'app-sprints-page',
  templateUrl: './sprints-page.component.html',
  imports: [
    CommonModule,
    DatePipe,
    ReactiveFormsModule,
    BoardColumnComponent,
    TaskDetailsModalComponent
  ]
})
export class SprintsPageComponent {
  loading = signal(false);
  error = signal<string | null>(null);
  users = signal<UserDto[]>([]);
  members = signal<MemberDto[]>([]);
  statuses = ['Open', 'InProgress', 'Blocked', 'Done'];
  projectId = signal<number>(0);
  projectName = signal<string>('');
  includeArchived = signal(false);

  sprints = signal<SprintDto[]>([]);
  selectedSprint = signal<SprintDto | null>(null);
  board = signal<SprintBoardDto | null>(null);

  showCreate = signal(false);
  showEdit = signal(false);

  selectedTask = signal<any | null>(null);
  showTaskDetails = signal(false);

  createForm: FormGroup;
  editForm: FormGroup;

  carryOverUnfinished = signal(true);

  constructor(
    private route: ActivatedRoute,
    private fb: FormBuilder,
    private api: SprintsApiService,
    private accountApi: AccountApiService,
    private tasksApi: TasksApiService,
    private auth: AuthStoreService,
    private projectsApi: ProjectsApiService,
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
    this.route.paramMap.subscribe(pm => {
      const idRaw = pm.get('projectId');
      const id = Number(idRaw ?? 0);

      this.projectId.set(id);
      this.projectName.set('');

      if (id) {
        this.projectsApi.getById(id).subscribe({
          next: p => this.projectName.set(p.name),
          error: () => this.projectName.set(''),
        });
      }
    });
    this.loadProjectMembers();
    this.loadSprints(true);
  }

  openTaskDetails(t: any) {
    this.selectedTask.set(t);
    this.showTaskDetails.set(true);
  }

  closeTaskDetails() {
    this.showTaskDetails.set(false);
    this.selectedTask.set(null);
  }

  onTaskDetailsSaved() {
    const s = this.selectedSprint();
    if (!s) return;
    this.loadBoard(s.id);
  }

  isCompanyAdmin(): boolean {
    return this.auth.isCompany();
  }

  loadSprints(selectActiveIfPossible: boolean) {
    const pid = this.projectId();
    if (!pid) return;

    const currentlySelectedId = this.selectedSprint()?.id ?? null;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.getByProject(pid, this.includeArchived())),
      tap((list) => {
        const s = list ?? [];
        s.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(s);

        if (currentlySelectedId != null) {
          const stillThere = s.find(x => x.id === currentlySelectedId) ?? null;
          if (stillThere) {
            this.selectSprint(stillThere);
            return;
          }
        }

        if (!selectActiveIfPossible) return;

        const active = s.find(x => x.isActive && !x.isArchived);
        if (active) { this.selectSprint(active); return; }

        const firstNonArchived = s.find(x => !x.isArchived);
        if (firstNonArchived) { this.selectSprint(firstNonArchived); return; }

        if (s.length) this.selectSprint(s[0]);
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

  setArchivedFlag(isArchived: boolean) {
    const s = this.selectedSprint();
    if (!s) return;

    const pid = this.projectId();

    if (isArchived && s.isActive) {
      this.error.set('Set the sprint inactive before archiving.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.api.setArchived(s.id, isArchived)),
      switchMap(() => this.api.getByProject(pid, this.includeArchived())),
      tap((list) => {
        const arr = list ?? [];
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);

        const updated = arr.find(x => x.id === s.id) ?? null;
        this.selectedSprint.set(updated);

        if (updated) this.loadBoard(s.id);
        else {
          this.board.set(null);
        }
      }),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to update sprint archive status.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  loadProjectMembers() {
    const projectId = this.projectId();
    if (!projectId) {
      this.members.set([]);
      this.users.set([]);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.projectsApi.getMembers(projectId)
      .pipe(
        tap(members => {
          this.members.set(members ?? []);

          this.users.set(
            (members ?? []).map(m => ({
              id: m.userId,
              name: m.userName,
              email: m.email
            } as UserDto))
          );
        }),
        catchError(err => {
          this.error.set('Failed to load project members.');
          this.members.set([]);
          this.users.set([]);
          return of([]);
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  taskStatus(t: any): string {
    return (t?.status ?? 'Open').toString();
  }

  selectSprint(s: SprintDto) {
    this.selectedSprint.set(s);
    this.showEdit.set(false);
    this.showCreate.set(false);
    this.loadBoard(s.id);
  }

  visibleSprints() {
    const all = this.sprints() ?? [];
    return this.isCompanyAdmin()
      ? all
      : all.filter(s => s.isActive);
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
      switchMap(() => this.loadBoardMerged$(s.id)),
      tap((b) => this.board.set(b)),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to set task status.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  assignFromBoard(t: any, assigneeUserId: string | null) {
    const s = this.selectedSprint();
    if (!s) return;

    const id = this.taskId(t);
    if (!id) return;

    const value = assigneeUserId ? assigneeUserId.toLowerCase() : null;

    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.tasksApi.assign(id, value)),
      switchMap(() => this.loadBoardMerged$(s.id)),
      tap((b) => this.board.set(b)),
      catchError((err) => {
        this.error.set(this.toMsg(err, 'Failed to assign task.'));
        return of(null);
      }),
      finalize(() => this.loading.set(false))
    ).subscribe();
  }

  private loadBoardMerged$(sprintId: number) {
    return this.api.board(sprintId).pipe(
      switchMap((b) =>
        this.tasksApi.getBySprint(sprintId).pipe(
          map((tasks) => {
            if (!b) return b;

            const assigneeMap = new Map<number, string | null>();
            (tasks ?? []).forEach(t =>
              assigneeMap.set(Number(t.id), t.assigneeUserId != null ? String(t.assigneeUserId) : null)
            );

            const merge = (arr: any[]) =>
              (arr ?? []).map(x => {
                const key = Number(this.taskId(x) ?? x.id);
                return {
                  ...x,
                  assigneeUserId: assigneeMap.get(key) ?? null,
                };
              });

            return {
              ...b,
              open: merge(b.open),
              inProgress: merge(b.inProgress),
              blocked: merge(b.blocked),
              done: merge(b.done),
              low: merge(b.low),
              medium: merge(b.medium),
              high: merge(b.high),
              critical: merge(b.critical),
            };
          })
        )
      )
    );
  }

  loadBoard(sprintId: number) {
    this.loading.set(true);
    this.error.set(null);

    of(null).pipe(
      switchMap(() => this.loadBoardMerged$(sprintId)),
      tap((b) => this.board.set(b)),
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
        return sel ? this.loadBoardMerged$(sel.id) : of(null);
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
      switchMap(() => this.api.getByProject(pid, this.includeArchived())),
      tap((list) => {
        const arr = list ?? [];
        arr.sort((a, b) => (b.id ?? 0) - (a.id ?? 0));
        this.sprints.set(arr);

        const updated = arr.find(x => x.id === s.id) ?? null;
        if (updated) this.selectedSprint.set(updated);
      }),
      switchMap(() => this.loadBoardMerged$(s.id)),
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

  onBoardDrop(ev: CdkDragDrop<any[]>) {
    if (ev.previousContainer === ev.container) {
      moveItemInArray(ev.container.data, ev.previousIndex, ev.currentIndex);
      return;
    }

    transferArrayItem(
      ev.previousContainer.data,
      ev.container.data,
      ev.previousIndex,
      ev.currentIndex
    );

    const map: Record<string, string> = {
      open: 'Open',
      inProgress: 'InProgress',
      blocked: 'Blocked',
      done: 'Done',
    };

    const task = ev.item.data;
    const newStatus = map[ev.container.id];
    if (newStatus) {
      this.setTaskStatusFromBoard(task, newStatus);
    }
  }
}
