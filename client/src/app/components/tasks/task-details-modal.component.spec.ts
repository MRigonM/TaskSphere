import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { environment } from '../../../environments/environment';
import { TaskDetailsModalComponent } from './task-details-modal.component';
import { TaskGitHubActivityDto } from '../../core/models/github-activity.models';
import { TaskGitHubActivityComponent } from './task-github-activity.component';

const activity: TaskGitHubActivityDto = {
  commits: [
    {
      sha: '1234567890abcdef',
      shortSha: '1234567',
      message: 'TS-42 wire the panel',
      authorName: 'Rigon',
      authorLogin: 'MRigonM',
      committedAtUtc: '2026-08-11T10:00:00Z',
      htmlUrl: 'https://github.com/rigon-org/api/commit/1234567',
      repositoryFullName: 'rigon-org/api',
      viaBranchName: null,
    },
  ],
  branches: [
    { name: 'TS-42-fix', headSha: 'abc', isDeleted: false, repositoryFullName: 'rigon-org/api' },
  ],
  pullRequests: [],
  lastSyncedAtUtc: '2026-08-12T07:00:00Z',
};

const emptyActivity: TaskGitHubActivityDto = {
  commits: [],
  branches: [],
  pullRequests: [],
  lastSyncedAtUtc: null,
};

/** Answers the refresh-on-open POST that now precedes every read of the activity panel. */
function flushRefresh(http: HttpTestingController, taskId: number) {
  http.expectOne(`${environment.apiUrl}Tasks/${taskId}/github-refresh`).flush({
    refreshed: true,
    repositoriesRefreshed: 0,
    tasksTransitioned: 0,
    lastSyncedAtUtc: null,
  });
}

async function setup(payload: TaskGitHubActivityDto = activity) {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({ token: 'a.b.c', name: 'Rigon', role: 'User', companyId: 1, userId: 'u1' })
  );

  TestBed.configureTestingModule({
    imports: [TaskDetailsModalComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(TaskDetailsModalComponent);

  fixture.componentRef.setInput('task', { id: 42, key: 'TS-42', title: 'Panel', status: 'Open' });
  fixture.componentRef.setInput('users', []);
  fixture.componentRef.setInput('sprints', []);
  fixture.detectChanges();

  flushRefresh(http, 42);
  http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(payload);
  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, http };
}

function tab(fixture: { nativeElement: HTMLElement }, name: string): HTMLButtonElement {
  return fixture.nativeElement.querySelector<HTMLButtonElement>(`[data-tab="${name}"]`)!;
}

describe('TaskDetailsModalComponent tabs', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController, null)?.verify();
    } finally {
      localStorage.clear();
      TestBed.resetTestingModule();
    }
  });

  it('opens on Details, with the form visible', async () => {
    const { fixture } = await setup();

    expect(fixture.nativeElement.querySelector('form')).toBeTruthy();
    expect(fixture.componentInstance.activeTab()).toBe('details');
  });

  it('loads the activity before the Activity tab is ever clicked', async () => {
    // The child stays mounted behind [hidden], so the count exists up front — one request per
    // modal open, and no separate count endpoint.
    const { fixture } = await setup();

    expect(tab(fixture, 'activity').textContent).toContain('2');
  });

  it('shows no badge when the task has no activity', async () => {
    const { fixture } = await setup(emptyActivity);

    expect(tab(fixture, 'activity').querySelector('[data-count]')).toBeNull();
  });

  it('switches to the Activity tab without re-requesting', async () => {
    const { fixture } = await setup();

    tab(fixture, 'activity').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.activeTab()).toBe('activity');
    expect(fixture.nativeElement.textContent).toContain('TS-42-fix');
    // verify() in afterEach fails if switching tabs fired a second read.
  });

  it('keeps the form mounted while the Activity tab is open, so edits survive a tab switch', async () => {
    const { fixture } = await setup();

    fixture.componentInstance.form.patchValue({ title: 'Edited but not saved' });

    tab(fixture, 'activity').click();
    fixture.detectChanges();

    // The value alone does not prove this: a FormGroup lives on the component and survives its
    // view being destroyed. The claim is about the DOM, so assert the DOM — the form is still
    // there, hidden, not torn down and rebuilt.
    const form = fixture.nativeElement.querySelector('form');
    expect(form).toBeTruthy();
    expect(form!.hasAttribute('hidden')).toBe(true);

    tab(fixture, 'details').click();
    fixture.detectChanges();

    // The tab actually went back — asserting only the form value lets a Details button that
    // sets 'activity' pass, since the value never depended on the tab.
    expect(fixture.componentInstance.activeTab()).toBe('details');
    expect(fixture.nativeElement.querySelector('form')!.hasAttribute('hidden')).toBe(false);
    expect(fixture.componentInstance.form.value.title).toBe('Edited but not saved');
  });

  it('returns to Details when a different task is opened', async () => {
    const { fixture, http } = await setup();

    tab(fixture, 'activity').click();
    fixture.detectChanges();

    fixture.componentRef.setInput('task', { id: 43, key: 'TS-43', title: 'Other', status: 'Open' });
    fixture.detectChanges();

    flushRefresh(http, 43);
    http.expectOne(`${environment.apiUrl}Tasks/43/github-activity`).flush(emptyActivity);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.activeTab()).toBe('details');
  });

  it('passes the activity panel tasksMoved on to its own host', async () => {
    const { fixture } = await setup();

    let moved = 0;
    fixture.componentInstance.tasksMoved.subscribe(() => moved++);

    // Raised on the real child through the template binding, not by calling a handler on the
    // modal: the binding is the thing that can go missing.
    const panel = fixture.debugElement
      .query((de) => de.componentInstance instanceof TaskGitHubActivityComponent)!
      .componentInstance as TaskGitHubActivityComponent;

    panel.tasksMoved.emit();
    fixture.detectChanges();

    expect(moved).toBe(1);
  });
});
