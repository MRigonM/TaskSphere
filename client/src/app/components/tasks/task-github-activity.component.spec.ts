import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { environment } from '../../../environments/environment';
import { TaskGitHubActivityComponent } from './task-github-activity.component';
import { PullRequestState, TaskGitHubActivityDto } from '../../core/models/github-activity.models';

const full: TaskGitHubActivityDto = {
  commits: [
    {
      sha: '1234567890abcdef1234567890abcdef12345678',
      shortSha: '1234567',
      message: 'TS-42 wire the panel\n\nWith a body nobody needs on one line.',
      authorName: 'Rigon',
      authorLogin: 'MRigonM',
      committedAtUtc: '2026-08-11T10:00:00Z',
      htmlUrl: 'https://github.com/rigon-org/api/commit/1234567',
      repositoryFullName: 'rigon-org/api',
    },
  ],
  branches: [
    { name: 'TS-42-fix', headSha: 'abcdefg', isDeleted: false, repositoryFullName: 'rigon-org/api' },
    { name: 'TS-42-old', headSha: 'hijklmn', isDeleted: true, repositoryFullName: 'rigon-org/api' },
  ],
  pullRequests: [
    {
      number: 17,
      title: 'TS-42 wire the panel',
      state: PullRequestState.Merged,
      authorLogin: 'MRigonM',
      openedAtUtc: '2026-08-10T09:00:00Z',
      mergedAtUtc: '2026-08-11T09:00:00Z',
      htmlUrl: 'https://github.com/rigon-org/api/pull/17',
      repositoryFullName: 'rigon-org/api',
    },
  ],
  lastSyncedAtUtc: '2026-08-12T07:00:00Z',
};

const empty: TaskGitHubActivityDto = {
  commits: [],
  branches: [],
  pullRequests: [],
  lastSyncedAtUtc: null,
};

/** The shape ApiBaseController.MapErrors actually returns: a list of { code, description }. */
function apiError(description: string) {
  return [{ code: 'GitHub.Failed', description }];
}

/** `nativeElement` is `any`, so a generic `querySelector` on it is TS2347. Type it once, here. */
function host(fixture: { nativeElement: HTMLElement }): HTMLElement {
  return fixture.nativeElement;
}

/** Mounts and renders once, leaving the first read in flight for the caller to answer. */
function mount(options: { role?: string } = {}) {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({
      token: 'a.b.c',
      name: 'Rigon',
      role: options.role ?? 'User',
      companyId: 1,
      userId: 'u1',
    })
  );

  TestBed.configureTestingModule({
    imports: [TaskGitHubActivityComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(TaskGitHubActivityComponent);
  fixture.componentRef.setInput('taskId', 42);
  fixture.detectChanges();

  return { fixture, http, req: http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`) };
}

async function setup(options: { payload?: TaskGitHubActivityDto; fail?: boolean; role?: string } = {}) {
  const { fixture, http, req } = mount({ role: options.role });

  if (options.fail) {
    req.flush(apiError('The activity could not be read.'), { status: 500, statusText: 'Server Error' });
  } else {
    req.flush(options.payload ?? full);
  }

  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, http };
}

function text(fixture: { nativeElement: HTMLElement }): string {
  return fixture.nativeElement.textContent ?? '';
}

describe('TaskGitHubActivityComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController, null)?.verify();
    } finally {
      localStorage.clear();
      TestBed.resetTestingModule();
    }
  });

  it('renders commits, branches and pull requests', async () => {
    const { fixture } = await setup();

    expect(text(fixture)).toContain('1234567');
    expect(text(fixture)).toContain('TS-42 wire the panel');
    expect(text(fixture)).toContain('TS-42-fix');
    expect(text(fixture)).toContain('#17');
  });

  it('renders only the first line of a commit message', async () => {
    const { fixture } = await setup();

    expect(text(fixture)).not.toContain('nobody needs on one line');
  });

  it('marks a branch GitHub no longer reports rather than dropping it', async () => {
    const { fixture } = await setup();

    const deleted = fixture.nativeElement.querySelector('[data-branch="TS-42-old"]');

    expect(deleted).toBeTruthy();
    expect(deleted!.textContent).toContain('deleted');
  });

  it('counts every record for the tab badge', async () => {
    const { fixture } = await setup();

    // 1 commit + 2 branches + 1 pull request.
    expect(fixture.componentInstance.count()).toBe(4);
  });

  it('shows the empty state for a genuinely empty payload', async () => {
    const { fixture } = await setup({ payload: empty });

    expect(text(fixture)).toContain('No GitHub activity for this task yet');
    expect(fixture.componentInstance.count()).toBe(0);
  });

  it('does not claim the task is empty while the first read is still in flight', async () => {
    // The unknown-vs-empty split, at the only point it is observable: with no error yet and no
    // data yet, a count of zero must not render as "nothing here". The failed-read test cannot
    // pin this — the template's `!error()` guard hides the empty state there either way.
    const { fixture, req } = mount();

    expect(fixture.componentInstance.count()).toBe(0);
    expect(fixture.componentInstance.isEmpty()).toBe(false);
    expect(text(fixture)).not.toContain('No GitHub activity for this task yet');

    req.flush(empty);
    await fixture.whenStable();
  });

  it('shows the error and NOT the empty state when the read fails', async () => {
    // data stays null — unknown, not empty. The panel must never render "no activity" over a
    // request that did not answer.
    const { fixture } = await setup({ fail: true });

    expect(text(fixture)).toContain('The activity could not be read.');
    expect(text(fixture)).not.toContain('No GitHub activity for this task yet');
    expect(fixture.componentInstance.count()).toBe(0);
  });

  it('retries the read from the error state', async () => {
    const { fixture, http } = await setup({ fail: true });

    host(fixture).querySelector<HTMLButtonElement>('[data-retry]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(full);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).not.toContain('The activity could not be read.');
    expect(text(fixture)).toContain('TS-42-fix');
  });

  it('re-reads when the task changes', async () => {
    const { fixture, http } = await setup();

    fixture.componentRef.setInput('taskId', 43);
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/43/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('No GitHub activity for this task yet');
  });

  it('says when it last looked, because ingestion is pull-based', async () => {
    const { fixture } = await setup();

    expect(text(fixture)).toContain('Last synced');
  });

  it('hides the sync button from a User-role caller', async () => {
    const { fixture } = await setup({ role: 'User' });

    expect(host(fixture).querySelector('[data-sync]')).toBeNull();
  });

  it('shows the sync button to a Company admin, and is honest that it is company-wide', async () => {
    const { fixture } = await setup({ role: 'Company' });

    const button = host(fixture).querySelector<HTMLButtonElement>('[data-sync]');

    expect(button).toBeTruthy();
    // The copy says "all repositories", not "this task": it spends installation rate limit
    // across the whole company despite sitting inside a task modal.
    expect(button!.textContent).toContain('all repositories');
  });

  it('re-reads the task activity after a successful sync', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 1,
      branches: 1,
      pullRequests: 1,
      linksCreated: 1,
      failures: [],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(full);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('TS-42-fix');
  });

  it('reports a partial failure without treating the run as failed', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      failures: [{ repositoryFullName: 'rigon-org/api', reason: 'GitHub returned 404.' }],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('rigon-org/api');
    expect(text(fixture)).toContain('GitHub returned 404.');
  });

  it('surfaces a failed sync as an error and does not re-read', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http
      .expectOne(`${environment.apiUrl}GitHub/activity/sync`)
      .flush(apiError('This company is not connected to GitHub.'), {
        status: 400,
        statusText: 'Bad Request',
      });

    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('This company is not connected to GitHub.');
    // verify() in afterEach fails if a re-read went out.
  });

  it('clears the previous run failures when a later sync fails', async () => {
    // The failure list belongs to the run that produced it. A failed sync never sets it, so
    // without the reset at the top of sync() the previous run's failures render underneath an
    // error about the current one — stale state attributed to the wrong run.
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      failures: [{ repositoryFullName: 'rigon-org/api', reason: 'GitHub returned 404.' }],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('GitHub returned 404.');

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}GitHub/activity/sync`)
      .flush(apiError('This company is not connected to GitHub.'), {
        status: 400,
        statusText: 'Bad Request',
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('This company is not connected to GitHub.');
    expect(text(fixture)).not.toContain('GitHub returned 404.');
  });
});
