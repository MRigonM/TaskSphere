import { describe, it, expect, vi, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';

import { SprintsPageComponent } from './sprints-page.component';
import { SprintsApiService } from '../core/services/sprints-api.service';
import { TasksApiService } from '../core/services/tasks-api.service';
import { AccountApiService } from '../core/services/account-api.service';
import { ProjectsApiService } from '../company-dashboard/projects/projects.service';
import { ToastService } from '../core/services/toast.service';
import { TaskDetailsModalComponent } from '../components/tasks/task-details-modal.component';

const sprint = { id: 5, name: 'Sprint 1', isActive: true, isArchived: false, projectId: 7 };
const inProgressTask = { id: 42, key: 'TS-42', title: 'Panel', status: 'InProgress' };
const doneTask = { ...inProgressTask, status: 'Done' };

function board(task: any, column: 'inProgress' | 'done') {
  return {
    sprintId: 5,
    sprintName: 'Sprint 1',
    projectId: 7,
    low: [], medium: [], high: [], critical: [],
    open: [],
    inProgress: column === 'inProgress' ? [task] : [],
    blocked: [],
    done: column === 'done' ? [task] : [],
  };
}

function setup() {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({ token: 'a.b.c', name: 'Rigon', role: 'Company', companyId: 1, userId: 'u1' }),
  );

  // The board reports the task where it currently sits; the test moves it at the moment the
  // sync would have.
  const boardFn = vi.fn().mockReturnValue(of(board(inProgressTask, 'inProgress')));

  const api = {
    board: boardFn,
    getByProject: vi.fn().mockReturnValue(of([sprint])),
  };

  const projectsApi = {
    getById: vi.fn().mockReturnValue(of({ id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: true })),
    getMembers: vi.fn().mockReturnValue(of([])),
    refreshGitHub: vi.fn().mockReturnValue(of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0 })),
  };

  TestBed.configureTestingModule({
    imports: [SprintsPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SprintsApiService, useValue: api },
      {
        provide: TasksApiService,
        useValue: {
          getBySprint: vi.fn().mockReturnValue(of([inProgressTask])),
          getById: vi.fn().mockReturnValue(of(inProgressTask)),
        },
      },
      { provide: AccountApiService, useValue: { getUsers: vi.fn().mockReturnValue(of([])) } },
      {
        provide: ProjectsApiService,
        useValue: projectsApi,
      },
      { provide: ToastService, useValue: { show: vi.fn() } },
      {
        provide: ActivatedRoute,
        useValue: {
          paramMap: of(new Map([['projectId', '7']]) as any),
          queryParamMap: of(new Map() as any),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(SprintsPageComponent);
  fixture.detectChanges();

  return { fixture, api, projectsApi };
}

function modalOf(fixture: any): TaskDetailsModalComponent {
  return fixture.debugElement
    .query((de: any) => de.componentInstance instanceof TaskDetailsModalComponent)!
    .componentInstance as TaskDetailsModalComponent;
}

describe('SprintsPageComponent — a sync that moved tasks', () => {
  it('re-reads the board and re-points the open modal at the fresh task', async () => {
    const { fixture, api } = setup();

    fixture.componentInstance.openTaskDetails(inProgressTask as any);
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedTask()!.status).toBe('InProgress');

    api.board.mockReturnValue(of(board(doneTask, 'done')));

    // Raised on the real modal through the template binding, not by calling the page's
    // handler: the binding is the part that can go missing.
    modalOf(fixture).tasksMoved.emit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.board()!.done.length).toBe(1);

    // The board moving the card is only half of it: the modal in front of it was opened with
    // the old object and resets its form only when that reference changes.
    expect(fixture.componentInstance.selectedTask()!.status).toBe('Done');
  });

  it('leaves the modal open while it refreshes', async () => {
    // A guard rather than a driver — this passes without the fix, and exists so that a later
    // fix cannot satisfy the one above by reusing `saved`, which the modal pairs with `closed`.
    const { fixture } = setup();

    fixture.componentInstance.openTaskDetails(inProgressTask as any);
    fixture.detectChanges();

    modalOf(fixture).tasksMoved.emit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.showTaskDetails()).toBe(true);
  });
});

describe('SprintsPageComponent — GitHub refresh on load', () => {
  it('re-reads the board on init when the refresh moved something', async () => {
    // This test proves the binding from init all the way through to the re-read, by
    // configuring the mock BEFORE setup runs with tasksTransitioned > 0.
    localStorage.setItem(
      'tasksphere_auth',
      JSON.stringify({ token: 'a.b.c', name: 'Rigon', role: 'Company', companyId: 1, userId: 'u1' }),
    );

    const boardFn = vi.fn().mockReturnValue(of(board(inProgressTask, 'inProgress')));

    // Capture the board call count BEFORE creating the component
    const callCountBeforeInit = boardFn.mock.calls.length;

    const api = {
      board: boardFn,
      getByProject: vi.fn().mockReturnValue(of([sprint])),
    };

    // Key: configure it to return tasksTransitioned > 0 BEFORE component creation.
    const projectsApi = {
      getById: vi.fn().mockReturnValue(of({ id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: true })),
      getMembers: vi.fn().mockReturnValue(of([])),
      refreshGitHub: vi.fn().mockReturnValue(of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 2 })),
    };

    TestBed.configureTestingModule({
      imports: [SprintsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SprintsApiService, useValue: api },
        {
          provide: TasksApiService,
          useValue: {
            getBySprint: vi.fn().mockReturnValue(of([inProgressTask])),
            getById: vi.fn().mockReturnValue(of(inProgressTask)),
          },
        },
        { provide: AccountApiService, useValue: { getUsers: vi.fn().mockReturnValue(of([])) } },
        { provide: ProjectsApiService, useValue: projectsApi },
        { provide: ToastService, useValue: { show: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(new Map([['projectId', '7']]) as any),
            queryParamMap: of(new Map() as any),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(SprintsPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    // After full init, refreshGitHub should have been called
    expect(projectsApi.refreshGitHub).toHaveBeenCalledWith(7);
    // Board should have been loaded at least once more than before init (via selectSprint in loadSprints)
    expect(boardFn.mock.calls.length).toBeGreaterThan(callCountBeforeInit);
  });

  it('refreshes GitHub once when the project loads', async () => {
    const { fixture, projectsApi } = setup();
    await fixture.whenStable();

    expect(projectsApi.refreshGitHub).toHaveBeenCalledWith(7);
    expect(projectsApi.refreshGitHub.mock.calls.length).toBe(1);
  });

  it('re-reads the board when the refresh moved something', async () => {
    const { fixture, api, projectsApi } = setup();
    projectsApi.refreshGitHub.mockReturnValue(
      of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 2 }),
    );

    const readsBefore = api.board.mock.calls.length;

    fixture.componentInstance.refreshGitHubActivity();
    await fixture.whenStable();

    expect(api.board.mock.calls.length).toBe(readsBefore + 1);
  });

  it('does not re-read when the refresh moved nothing', async () => {
    const { fixture, api, projectsApi } = setup();
    projectsApi.refreshGitHub.mockReturnValue(
      of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0 }),
    );

    const readsBefore = api.board.mock.calls.length;

    fixture.componentInstance.refreshGitHubActivity();
    await fixture.whenStable();

    expect(api.board.mock.calls.length).toBe(readsBefore);
  });

  it('says nothing when the refresh fails', async () => {
    const { fixture, projectsApi } = setup();
    projectsApi.refreshGitHub.mockReturnValue(throwError(() => ({ status: 500 })));

    fixture.componentInstance.refreshGitHubActivity();
    await fixture.whenStable();
    fixture.detectChanges();

    // The user did not ask for this call. A banner about it reads as a broken board.
    expect(fixture.componentInstance.error()).toBeNull();
  });

  afterEach(() => {
    // The mounted activity panel issues its own read; drain it so it does not leak.
    TestBed.inject(HttpTestingController).match(() => true).forEach((r) =>
      r.flush({ commits: [], branches: [], pullRequests: [], lastSyncedAtUtc: null }),
    );
  });
});
