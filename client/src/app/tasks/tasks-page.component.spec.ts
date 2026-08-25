import { describe, it, expect, vi, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';

import { TasksPageComponent } from './tasks-page.component';
import { TasksApiService } from '../core/services/tasks-api.service';
import { SprintsApiService } from '../core/services/sprints-api.service';
import { AccountApiService } from '../core/services/account-api.service';
import { ProjectsApiService } from '../company-dashboard/projects/projects.service';
import { ToastService } from '../core/services/toast.service';
import { TaskDetailsModalComponent } from '../components/tasks/task-details-modal.component';

const openTask = { id: 42, key: 'TS-42', title: 'Panel', status: 'InProgress', projectId: 7 };
const doneTask = { ...openTask, status: 'Done' };

function setup() {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({ token: 'a.b.c', name: 'Rigon', role: 'Company', companyId: 1, userId: 'u1' }),
  );
  // exactly what a sync that moved it looks like from the client's side.
  // The backlog reports the task as the caller last left it; the test changes the answer at
  // the moment the sync would have moved it.
  const getBacklog = vi.fn().mockReturnValue(of([openTask]));

  const tasksApi = {
    getBacklog,
    getBySprint: vi.fn().mockReturnValue(of([])),
    getById: vi.fn().mockReturnValue(of(openTask)),
  };

  TestBed.configureTestingModule({
    imports: [TasksPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: TasksApiService, useValue: tasksApi },
      { provide: SprintsApiService, useValue: { getByProject: vi.fn().mockReturnValue(of([])) } },
      { provide: AccountApiService, useValue: { getUsers: vi.fn().mockReturnValue(of([])) } },
      {
        provide: ProjectsApiService,
        useValue: { getById: vi.fn().mockReturnValue(of({ id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: true })) },
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

  const fixture = TestBed.createComponent(TasksPageComponent);
  fixture.detectChanges();

  return { fixture, tasksApi };
}

describe('TasksPageComponent — a sync that moved tasks', () => {
  it('re-reads the backlog and re-points the open modal at the fresh task', async () => {
    const { fixture, tasksApi } = setup();

    fixture.componentInstance.openTaskDetails(openTask as any);
    fixture.detectChanges();

    const readsBefore = tasksApi.getBacklog.mock.calls.length;

    // From here on the server reports the task as Done — what a sync that moved it looks
    // like from the client's side.
    tasksApi.getBacklog.mockReturnValue(of([doneTask]));

    // Raised on the real modal through the template binding, not by calling the page's
    // handler: the binding is the part that can go missing.
    const modal = fixture.debugElement
      .query((de) => de.componentInstance instanceof TaskDetailsModalComponent)!
      .componentInstance as TaskDetailsModalComponent;

    modal.tasksMoved.emit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(tasksApi.getBacklog.mock.calls.length).toBe(readsBefore + 1);

    // The board reloading is only half of it: selectedTask still referenced the pre-reload
    // object, so the modal the user is looking at kept showing the old status. It resets its
    // form in ngOnChanges, which fires only when the reference changes.
    expect(fixture.componentInstance.selectedTask()!.status).toBe('Done');
  });

  it('leaves the modal open while it refreshes', async () => {
    const { fixture } = setup();

    fixture.componentInstance.openTaskDetails(openTask as any);
    fixture.detectChanges();

    const modal = fixture.debugElement
      .query((de) => de.componentInstance instanceof TaskDetailsModalComponent)!
      .componentInstance as TaskDetailsModalComponent;

    modal.tasksMoved.emit();
    await fixture.whenStable();
    fixture.detectChanges();

    // Unlike `saved`, which the modal pairs with `closed`, this fires while the user is still
    // reading the activity tab.
    expect(fixture.componentInstance.showTaskDetails()).toBe(true);
  });

  afterEach(() => {
    // The mounted activity panel issues its own read; drain it so it does not leak into the
    // next test.
    TestBed.inject(HttpTestingController).match(() => true).forEach((r) => r.flush({
      commits: [], branches: [], pullRequests: [], lastSyncedAtUtc: null,
    }));
  });
});
