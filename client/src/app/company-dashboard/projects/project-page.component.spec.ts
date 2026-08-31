import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ProjectPageComponent } from './project-page.component';
import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';
import { ToastService } from '../../core/services/toast.service';
import { environment } from '../../../environments/environment';
import { ProjectDto } from '../../core/models/projects.models';

const project: ProjectDto = { id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: false };

/**
 * `role` seeds the auth store the way the links screen's spec does — `AuthStoreService` reads
 * localStorage at construction, so it has to be written before the TestBed builds the component.
 * Left null the component renders as a signed-out page would: no admin-only reads at all.
 */
function setup(current: ProjectDto = project, role: 'Company' | 'User' | null = null) {
  const toast = { show: vi.fn() };

  if (role) {
    localStorage.setItem(
      'tasksphere_auth',
      JSON.stringify({ token: 'a.b.c', name: 'Rigon', role, companyId: 1, userId: 'u1' }),
    );
  }

  const projectsApi = {
    getAll: vi.fn().mockReturnValue(of([current])),
    getMembers: vi.fn().mockReturnValue(of([])),
    updateSettings: vi.fn().mockReturnValue(of({ ...current, autoDoneOnMerge: true })),
  };

  TestBed.configureTestingModule({
    imports: [ProjectPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ProjectsApiService, useValue: projectsApi },
      { provide: AccountApiService, useValue: { getUsers: vi.fn().mockReturnValue(of([])) } },
      { provide: ToastService, useValue: toast },
      {
        provide: ActivatedRoute,
        useValue: { paramMap: of(new Map([['projectId', '7']]) as any) },
      },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  const fixture: ComponentFixture<ProjectPageComponent> =
    TestBed.createComponent(ProjectPageComponent);
  fixture.detectChanges();

  return { fixture, projectsApi, toast, http };
}

function flushProjectRepositories(
  http: HttpTestingController,
  body: { links: unknown[]; unavailableCount: number } = { links: [], unavailableCount: 0 },
) {
  http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(body);
}

function flushConnection(
  http: HttpTestingController,
  repositorySelection: 0 | 1,
  repositories: unknown[] = [],
) {
  http.expectOne(`${environment.apiUrl}GitHub/connection`).flush({
    installation: {
      id: 1,
      installationId: 42,
      accountLogin: 'acme-corp',
      accountType: 'Organization',
      repositorySelection,
      isSuspended: false,
    },
    repositories,
  });
}

function checkbox(fixture: ComponentFixture<ProjectPageComponent>): HTMLInputElement {
  return fixture.nativeElement.querySelector('[data-testid="auto-done-on-merge"]');
}

describe('ProjectPageComponent — the auto-done toggle', () => {
  let harness: ReturnType<typeof setup>;

  beforeEach(() => {
    harness = setup();
  });

  it('renders the toggle from the loaded project', () => {
    expect(checkbox(harness.fixture)).toBeTruthy();
    expect(checkbox(harness.fixture).checked).toBe(false);
  });

  it('sends the new value when the box is ticked in the template', () => {
    // Driven through the DOM, not by calling the handler: a test that calls the handler
    // directly cannot witness the (change) binding, which is exactly the gap the 2026-08-23
    // sweep found as M05.
    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    expect(harness.projectsApi.updateSettings).toHaveBeenCalledWith(7, true);
  });

  it('renders the toggle from the server response rather than from the click', () => {
    // The server is the authority: the box shows what came back, not what was clicked.
    harness.projectsApi.updateSettings.mockReturnValue(
      of({ ...project, autoDoneOnMerge: true }),
    );

    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    expect(harness.fixture.componentInstance.project()!.autoDoneOnMerge).toBe(true);
    expect(checkbox(harness.fixture).checked).toBe(true);
  });

  it('puts the box back and explains itself when the save fails', () => {
    harness.projectsApi.updateSettings.mockReturnValue(
      throwError(() => ({ status: 500 })),
    );

    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    // Without the explicit revert the browser leaves the box ticked while nothing was saved,
    // and the bound signal is unchanged so Angular never corrects it.
    expect(checkbox(harness.fixture).checked).toBe(false);
    expect(harness.fixture.componentInstance.error()).toContain('Failed to update project settings');
  });

  it('confirms what was turned on, not the opposite', () => {
    // The two messages are each other's negation, so swapping the branches is invisible
    // unless a test pins the message to the state it describes. The mutation sweep found
    // exactly that swap surviving.
    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    expect(harness.toast.show).toHaveBeenCalledWith(
      'Merged pull requests will move their task to Done',
      'info',
    );
  });

  it('confirms what was turned off', () => {
    // beforeEach already instantiated the TestBed for the default project; this test needs
    // one that starts enabled, so the module is reset before it is reconfigured.
    TestBed.resetTestingModule();

    const enabled: ProjectDto = { ...project, autoDoneOnMerge: true };
    const off = setup(enabled);
    off.projectsApi.updateSettings.mockReturnValue(of({ ...enabled, autoDoneOnMerge: false }));

    expect(checkbox(off.fixture).checked).toBe(true);

    checkbox(off.fixture).click();
    off.fixture.detectChanges();

    expect(off.toast.show).toHaveBeenCalledWith(
      'Merged pull requests will no longer move their task',
      'info',
    );
  });
});

function becomeVisible() {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
  document.dispatchEvent(new Event('visibilitychange'));
}

describe('ProjectPageComponent — the repositories section', () => {
  afterEach(() => {
    // AuthStoreService reads this at construction, so a leaked role would silently change the
    // role every later test in this run believes it is running as.
    localStorage.removeItem('tasksphere_auth');
    vi.restoreAllMocks();
  });

  it('lists the repositories linked to the project', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http, {
      links: [
        { id: 1, projectId: 7, gitHubRepositoryId: 3, fullName: 'acme-corp/api', linkedByUserId: 'u1' },
      ],
      unavailableCount: 0,
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('acme-corp/api');
  });

  it('reports links whose repository is no longer available', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http, { links: [], unavailableCount: 2 });
    fixture.detectChanges();

    // The count is the only thing the server can say about them, and saying nothing would make
    // a link silently vanish.
    const notice: HTMLElement = fixture.nativeElement.querySelector('[data-testid="unavailable-count"]');
    expect(notice?.textContent).toContain('2');
  });

  it('offers only repositories that are not already linked, to an admin', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http, {
      links: [
        { id: 1, projectId: 7, gitHubRepositoryId: 3, fullName: 'acme-corp/api', linkedByUserId: 'u1' },
      ],
      unavailableCount: 0,
    });
    flushConnection(http, 0, [
      { id: 3, repositoryId: 300, fullName: 'acme-corp/api', defaultBranch: 'main', isPrivate: true },
      { id: 4, repositoryId: 400, fullName: 'acme-corp/web', defaultBranch: 'main', isPrivate: false },
    ]);
    fixture.detectChanges();

    expect(fixture.componentInstance.linkable().map(r => r.fullName)).toEqual(['acme-corp/web']);
  });

  it('does not read the company connection for a member', () => {
    const { http } = setup(project, 'User');

    flushProjectRepositories(http);

    // The connection endpoint is Company-gated, so a member requesting it earns a guaranteed
    // 403 and an error message about something they cannot act on.
    http.expectNone(`${environment.apiUrl}GitHub/connection`);
    http.verify();
  });

  it('re-reads the links from the server after a successful link', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http);
    flushConnection(http, 0, [
      { id: 4, repositoryId: 400, fullName: 'acme-corp/web', defaultBranch: 'main', isPrivate: false },
    ]);
    fixture.detectChanges();

    fixture.componentInstance.linkRepository(4);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush({
      id: 9, projectId: 7, gitHubRepositoryId: 4, fullName: 'acme-corp/web', linkedByUserId: 'u1',
    });

    // Refetched rather than patched: unavailableCount is derivable only server-side, so a local
    // edit could leave the section stating something the server would contradict.
    flushProjectRepositories(http, {
      links: [
        { id: 9, projectId: 7, gitHubRepositoryId: 4, fullName: 'acme-corp/web', linkedByUserId: 'u1' },
      ],
      unavailableCount: 0,
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('acme-corp/web');
    expect(fixture.componentInstance.linkable()).toEqual([]);
    http.verify();
  });

  it('explains a failed unlink instead of dropping the row', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http, {
      links: [
        { id: 1, projectId: 7, gitHubRepositoryId: 3, fullName: 'acme-corp/api', linkedByUserId: 'u1' },
      ],
      unavailableCount: 0,
    });
    flushConnection(http, 0);
    fixture.detectChanges();

    fixture.componentInstance.unlinkRepository(3);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories/3`).flush(
      [{ code: 'GitHub.Failed', description: 'The link could not be removed.' }],
      { status: 500, statusText: 'Server Error' },
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.repositoriesError()).toContain('could not be removed');
    // The row is still there: nothing was removed, and hiding it would claim otherwise.
    expect(fixture.nativeElement.textContent).toContain('acme-corp/api');
    http.verify();
  });

  it('offers the GitHub link when the installation has only selected repositories', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http);
    flushConnection(http, 0); // RepositorySelection.Selected
    fixture.detectChanges();

    expect(fixture.componentInstance.manageUrl()).toBe(
      'https://github.com/organizations/acme-corp/settings/installations/42',
    );
    expect(fixture.nativeElement.textContent).toContain('Manage repository access on GitHub');
  });

  it('offers no GitHub link when the installation already has every repository', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http);
    flushConnection(http, 1); // RepositorySelection.All
    fixture.detectChanges();

    // Nothing to grant over there — the list only needs refreshing.
    expect(fixture.componentInstance.manageUrl()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Manage repository access on GitHub');
  });

  it('re-syncs the repositories once when the user returns from GitHub', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http);
    flushConnection(http, 0);
    fixture.detectChanges();

    vi.spyOn(window, 'open').mockReturnValue(null);
    fixture.componentInstance.openGitHubSettings();

    becomeVisible();

    http.expectOne(`${environment.apiUrl}GitHub/repositories/sync`).flush({
      installation: null,
      repositories: [],
    });
    // The project's own links are re-read too: a newly granted repository is linkable, but the
    // linked list is what the section renders.
    flushProjectRepositories(http);
    http.verify();
  });

  it('stops listening for the return once the page is destroyed', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http);
    flushConnection(http, 0);
    fixture.detectChanges();

    vi.spyOn(window, 'open').mockReturnValue(null);
    fixture.componentInstance.openGitHubSettings();
    fixture.destroy();

    becomeVisible();

    // returned$ never completes, so without takeUntilDestroyed a navigated-away page keeps
    // syncing — and keeps winning the arm from whatever page the user is actually on.
    http.expectNone(`${environment.apiUrl}GitHub/repositories/sync`);
    http.verify();
  });

  it('does not sync on a refocus the user never armed', () => {
    const { fixture, http } = setup(project, 'Company');

    flushProjectRepositories(http);
    flushConnection(http, 0);
    fixture.detectChanges();

    // An ordinary alt-tab back to the app. Spending a GitHub call here is exactly what the arm
    // exists to prevent.
    becomeVisible();

    http.expectNone(`${environment.apiUrl}GitHub/repositories/sync`);
    http.verify();
  });
});
