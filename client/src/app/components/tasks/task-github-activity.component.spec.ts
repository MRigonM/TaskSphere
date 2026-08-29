import { describe, it, expect, afterEach, vi } from 'vitest';
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
      viaBranchName: null,
    },
  ],
  branches: [
    { name: 'TS-42-fix', headSha: 'abcdefg', isDeleted: false, repositoryFullName: 'rigon-org/api' },
    { name: 'TS-42-old', headSha: 'hijklmn', isDeleted: true, repositoryFullName: 'rigon-org/api' },
  ],
  pullRequests: [
    {
      number: 17,
      // Deliberately NOT the commit's subject line. When the two matched, "renders commits,
      // branches and pull requests" was satisfied by the pull request alone, and a mutant that
      // rendered the wrong line of the commit message survived.
      title: 'TS-42 add the endpoint',
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

  // Every open now refreshes before it reads. Tests that only care about the read flush this
  // with a no-op result and move on to the request they actually asserted on.
  flushRefresh(http, 42);

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

/** Mount as a member (User role) and leave the first read in flight. */
function setupAsMember() {
  return mount({ role: 'User' });
}

/** Answer the activity read request. */
function flushActivity(req: any, body: any = full) {
  req.flush(body);
}

/**
 * Answers the refresh-on-open POST that now precedes every read. A shared helper because
 * every test that triggers a load — mount, or a later task change — now has one of these to
 * flush before the GET it actually cares about shows up.
 */
function flushRefresh(http: HttpTestingController, taskId: number) {
  http.expectOne(`${environment.apiUrl}Tasks/${taskId}/github-refresh`).flush({
    refreshed: true,
    repositoriesRefreshed: 0,
    tasksTransitioned: 0,
    lastSyncedAtUtc: null,
  });
}

/** Mounts the component without triggering change detection, for tests that drive ngOnChanges by hand. */
function createComponent(options: { role?: string } = {}) {
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

  return { component: fixture.componentInstance, http, fixture };
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

    // Both halves matter: the body is dropped AND the subject survives. Asserting only the
    // absence lets `split('\n')[1]` — which renders the blank second line — pass.
    expect(text(fixture)).toContain('TS-42 wire the panel');
    expect(text(fixture)).not.toContain('nobody needs on one line');
  });

  it('labels a pull request by its state', async () => {
    const { fixture } = await setup({
      payload: {
        ...empty,
        pullRequests: [
          { ...full.pullRequests[0], number: 1, state: PullRequestState.Open },
          { ...full.pullRequests[0], number: 2, state: PullRequestState.Closed },
          { ...full.pullRequests[0], number: 3, state: PullRequestState.Merged },
        ],
      },
    });

    // Per row, not across the panel: asserting the three words appear somewhere passes under
    // every permutation of the mapping, including one that labels a merged PR "Open".
    expect(host(fixture).querySelector('[data-pr="1"]')!.textContent).toContain('Open');
    expect(host(fixture).querySelector('[data-pr="2"]')!.textContent).toContain('Closed');
    expect(host(fixture).querySelector('[data-pr="3"]')!.textContent).toContain('Merged');
  });

  it('renders no section headings and no sync date for an empty payload', async () => {
    // An empty payload must not leave three empty sections behind, and must not claim a last
    // sync that never happened — `lastSyncedAtUtc` is null until one has run.
    const { fixture } = await setup({ payload: empty });

    expect(text(fixture)).not.toContain('Pull requests');
    expect(text(fixture)).not.toContain('Branches');
    expect(text(fixture)).not.toContain('Commits');
    expect(text(fixture)).not.toContain('Last synced');
  });

  it('stops saying it is loading once the read has failed', async () => {
    const { fixture } = await setup({ fail: true });

    expect(text(fixture)).not.toContain('Loading activity');
  });

  it('does not read anything when there is no task to read', async () => {
    localStorage.setItem(
      'tasksphere_auth',
      JSON.stringify({ token: 'a.b.c', name: 'Rigon', role: 'User', companyId: 1, userId: 'u1' })
    );
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    const fixture = TestBed.createComponent(TaskGitHubActivityComponent);
    fixture.componentRef.setInput('taskId', undefined);
    fixture.detectChanges();

    // Without the `currentValue` guard this fires GET Tasks/undefined/github-activity, and
    // verify() in afterEach is what catches it.
    expect(fixture.componentInstance.data()).toBeNull();
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

    flushRefresh(http, 43);
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
      tasksTransitioned: 0,
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
      tasksTransitioned: 0,
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

  it('disables the sync button while a sync is in flight', async () => {
    // The call spends company-wide installation rate limit. A second click before the first
    // answers spends it twice.
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    const button = host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!;
    button.click();
    fixture.detectChanges();

    expect(button.disabled).toBe(true);

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      tasksTransitioned: 0,
      failures: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.disabled).toBe(false);
  });

  it('names the branch when a failure is branch-scoped, and only then', async () => {
    // Partial success is branch-level since 2026-08-19: a repository can be fully synced except
    // for one branch's commits, and the panel has the branch name either way.
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 2,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      tasksTransitioned: 0,
      failures: [
        { repositoryFullName: 'rigon-org/api', reason: 'Commits listing failed.', branch: 'TS-42-fix' },
        { repositoryFullName: 'rigon-org/web', reason: 'GitHub returned 404.', branch: null },
      ],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('@TS-42-fix');
    expect(text(fixture)).not.toContain('rigon-org/web@');
  });

  it('drops the previous task activity when a new task fails to load', async () => {
    // Otherwise task 42's commits stay on screen under an error about task 43 — one task's
    // history attributed to another.
    const { fixture, http } = await setup();

    expect(text(fixture)).toContain('TS-42-fix');

    fixture.componentRef.setInput('taskId', 43);
    fixture.detectChanges();

    flushRefresh(http, 43);
    http
      .expectOne(`${environment.apiUrl}Tasks/43/github-activity`)
      .flush(apiError('The activity could not be read.'), { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('The activity could not be read.');
    expect(text(fixture)).not.toContain('TS-42-fix');
    expect(text(fixture)).not.toContain('1234567');
  });

  it('keeps the task activity on screen when a refresh of the same task fails', async () => {
    // The other half of the task-change rule: for the SAME task, a failed re-read means "could
    // not refresh", not "there is nothing". The last known activity stays under the error.
    const { fixture, http } = await setup({ role: 'Company' });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      tasksTransitioned: 0,
      failures: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    http
      .expectOne(`${environment.apiUrl}Tasks/42/github-activity`)
      .flush(apiError('The activity could not be read.'), { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('The activity could not be read.');
    expect(text(fixture)).toContain('TS-42-fix');
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
      tasksTransitioned: 0,
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

  it('offers the create-branch button to a member, who has no sync button', async () => {
    // Members see the panel and may create a branch: the repo↔project link authorizes it.
    // Sync stays admin-only — it is company-wide and spends installation rate limit.
    const { fixture, http, req } = setupAsMember();
    flushActivity(req);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(host(fixture).querySelector('[data-create-branch]')).not.toBeNull();
    expect(host(fixture).querySelector('[data-sync]')).toBeNull();
  });

  it('keeps the company-wide Sync button for admins only', () => {
    const { component, fixture } = createComponent();

    component.isCompanyAdmin = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-sync]')).toBeTruthy();
  });

  it('does not show the Sync button to a member, who no longer needs it', () => {
    const { component, fixture } = createComponent();

    component.isCompanyAdmin = false;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-sync]')).toBeFalsy();
  });

  it('opens the dialog on click', async () => {
    const { fixture, http, req } = setupAsMember();
    flushActivity(req);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-create-branch-dialog')).toBeNull();

    fixture.nativeElement.querySelector('[data-create-branch]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-create-branch-dialog')).not.toBeNull();

    // The dialog makes a suggestion request when it mounts; flush it before exiting the test.
    http.expectOne(`${environment.apiUrl}Tasks/42/github-branch/suggestion`).flush({
      taskKey: 'TS-42',
      suggestedName: 'TS-42/crud-for-product',
      repositories: [{ id: 7, fullName: 'rigon-org/api', defaultBranch: 'main' }],
    });
  });

  it('re-reads the activity after a branch is created', async () => {
    const { fixture, http, req } = setupAsMember();
    flushActivity(req);
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.onBranchCreated();

    // A second read, through the same load() every other refresh uses — the new branch and
    // its link arrive from the server rather than being spliced in client-side.
    flushActivity(http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.showBranchDialog()).toBe(false);
  });

  it('says how many tasks the sync moved to Done', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      // Deliberately different from tasksTransitioned: with both at 3, asserting that "3"
      // appears somewhere would pass without the count being rendered at all.
      pullRequests: 5,
      linksCreated: 5,
      tasksTransitioned: 3,
      failures: [],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    // The number next to its own label, not merely present on the page.
    const banner = host(fixture).querySelector('[data-transitioned]')!;
    expect(banner.textContent).toContain('3 tasks moved to Done');
  });

  it('says nothing about transitions when the sync moved none', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      tasksTransitioned: 0,
      failures: [],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    // "0 tasks moved to Done" is noise on every ordinary sync.
    expect(host(fixture).querySelector('[data-transitioned]')).toBeNull();
  });

  it('tells its parent when a sync moved tasks, so the board can re-read them', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    let moved = 0;
    fixture.componentInstance.tasksMoved.subscribe(() => moved++);

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 1,
      linksCreated: 0,
      tasksTransitioned: 1,
      failures: [],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    // Without this the panel renders "1 task moved to Done" while the board behind it still
    // shows the task in its old column, and only a page reload fixes it.
    expect(moved).toBe(1);
  });

  it('does not ask the board to re-read when the sync moved nothing', async () => {
    const { fixture, http } = await setup({ role: 'Company', payload: empty });

    let moved = 0;
    fixture.componentInstance.tasksMoved.subscribe(() => moved++);

    host(fixture).querySelector<HTMLButtonElement>('[data-sync]')!.click();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 0,
      pullRequests: 0,
      linksCreated: 0,
      tasksTransitioned: 0,
      failures: [],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(moved).toBe(0);
  });

  it('marks an inherited commit with the branch that conferred it, and leaves a direct one bare', async () => {
    // Two commits, so the assertion can prove the marker landed on the RIGHT one. With a single
    // fixture commit, "the text appears somewhere" is true of a marker rendered on every row.
    const activity = {
      commits: [
        { sha: 'direct1', shortSha: 'direct1', message: 'TS-42 add the form', authorName: 'Rigon', authorLogin: 'MRigonM', committedAtUtc: '2026-08-27T10:00:00Z', htmlUrl: 'https://example.invalid/1', repositoryFullName: 'rigon-org/api', viaBranchName: null },
        { sha: 'ahead1', shortSha: 'ahead1', message: 'wire up the login form', authorName: 'Rigon', authorLogin: 'MRigonM', committedAtUtc: '2026-08-27T11:00:00Z', htmlUrl: 'https://example.invalid/2', repositoryFullName: 'rigon-org/api', viaBranchName: 'TS-42-login' },
      ],
      branches: [],
      pullRequests: [],
      lastSyncedAtUtc: null,
    };

    const { fixture } = await setup({ payload: activity as TaskGitHubActivityDto });

    // `host()` exists because `nativeElement` is `any` and a generic querySelector on it is TS2347.
    const rows = host(fixture).querySelectorAll('section li');

    expect(rows[0].querySelector('[data-via-branch]')).toBeNull();
    expect(rows[1].querySelector('[data-via-branch]')?.textContent?.trim()).toBe('via TS-42-login');
  });

  it('refreshes before reading, so the panel shows what GitHub has now', () => {
    const { component, http } = createComponent();

    component.taskId = 42;
    component.ngOnChanges({ taskId: { currentValue: 42, previousValue: undefined, firstChange: true, isFirstChange: () => true } });

    const refresh = http.expectOne(`${environment.apiUrl}Tasks/42/github-refresh`);
    expect(refresh.request.method).toBe('POST');
    refresh.flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0, lastSyncedAtUtc: null });

    const read = http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`);
    expect(read.request.method).toBe('GET');
    read.flush(empty);
  });

  it('still reads when the refresh fails, so a GitHub outage does not blank the panel', () => {
    const { component, http } = createComponent();

    component.taskId = 42;
    component.ngOnChanges({ taskId: { currentValue: 42, previousValue: undefined, firstChange: true, isFirstChange: () => true } });

    http
      .expectOne(`${environment.apiUrl}Tasks/42/github-refresh`)
      .flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });

    // The mirror still holds whatever the last successful sync wrote. Showing it beats showing
    // an error over data that exists.
    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
  });

  it('tells the board when the refresh moved a task to Done', () => {
    const { component, http } = createComponent();
    const moved = vi.fn();
    component.tasksMoved.subscribe(moved);

    component.taskId = 42;
    component.ngOnChanges({ taskId: { currentValue: 42, previousValue: undefined, firstChange: true, isFirstChange: () => true } });

    http
      .expectOne(`${environment.apiUrl}Tasks/42/github-refresh`)
      .flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 1, lastSyncedAtUtc: null });

    // Without this the panel says a task moved while the board behind it shows the old column
    // until a page reload — the 2026-08-25 defect, reached through a new trigger.
    expect(moved).toHaveBeenCalled();

    http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`).flush(empty);
  });
});
