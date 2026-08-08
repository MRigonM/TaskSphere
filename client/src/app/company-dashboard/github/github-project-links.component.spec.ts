import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { GitHubProjectLinksComponent } from './github-project-links.component';
import { GitHubConnectionService } from '../../core/services/github-connection.service';
import { environment } from '../../../environments/environment';
import { GitHubConnectionDto, ProjectRepositoriesDto } from '../../core/models/github.models';
import { ProjectDto } from '../../core/models/projects.models';

const projects: ProjectDto[] = [
  { id: 7, name: 'Apollo', key: 'APO' },
  { id: 8, name: 'Borealis', key: 'BOR' },
];

/** `id` is the local GitHubRepository PK — the id the link endpoints take. */
const connected: GitHubConnectionDto = {
  installation: {
    id: 1,
    installationId: 42,
    accountLogin: 'acme-corp',
    accountType: 'Organization',
    repositorySelection: 0,
    isSuspended: false,
  },
  repositories: [
    { id: 1, repositoryId: 100, fullName: 'acme-corp/api', defaultBranch: 'main', isPrivate: true },
    { id: 2, repositoryId: 101, fullName: 'acme-corp/web', defaultBranch: 'develop', isPrivate: false },
  ],
};

const webLinked: ProjectRepositoriesDto = {
  links: [
    { id: 10, projectId: 7, gitHubRepositoryId: 2, fullName: 'acme-corp/web', linkedByUserId: 'u1' },
  ],
  unavailableCount: 0,
};

/** The shape ApiBaseController.MapErrors actually returns: a list of { code, description }. */
function apiError(description: string) {
  return [{ code: 'GitHub.Failed', description }];
}

async function setup(
  seed: GitHubConnectionDto = connected,
  options: { role?: string; projects?: 'ok' | 'error' } = {}
) {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({
      token: 'a.b.c',
      name: 'Rigon',
      role: options.role ?? 'Company',
      companyId: 1,
      userId: 'u1',
    })
  );

  TestBed.configureTestingModule({
    imports: [GitHubProjectLinksComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const http = TestBed.inject(HttpTestingController);

  // The installation's repositories come from the root connection signal, never a refetch.
  TestBed.inject(GitHubConnectionService).loadConnection().subscribe();
  http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(seed);

  const fixture = TestBed.createComponent(GitHubProjectLinksComponent);
  fixture.detectChanges();

  const req = http.expectOne(`${environment.apiUrl}Projects/`);
  if (options.projects === 'error') {
    req.flush(apiError('The projects could not be read.'), {
      status: 500,
      statusText: 'Server Error',
    });
  } else {
    req.flush(projects);
  }

  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, http };
}

function projectSelect(fixture: { nativeElement: HTMLElement }): HTMLSelectElement {
  return fixture.nativeElement.querySelector('select') as HTMLSelectElement;
}

function retryButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement | null {
  return (
    Array.from(fixture.nativeElement.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Try again')
    ) ?? null
  );
}

async function chooseProject(fixture: any, projectId: number) {
  const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
  select.value = String(projectId);
  select.dispatchEvent(new Event('change'));
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Linked and available are disjoint, so a full name identifies exactly one row. */
function row(fixture: { nativeElement: HTMLElement }, fullName: string): HTMLElement | null {
  return (
    Array.from(fixture.nativeElement.querySelectorAll('li')).find(li =>
      li.textContent?.includes(fullName)
    ) ?? null
  );
}

function rowButton(fixture: { nativeElement: HTMLElement }, fullName: string) {
  return row(fixture, fullName)?.querySelector('button') ?? null;
}

describe('GitHubProjectLinksComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      localStorage.removeItem('tasksphere_auth');
    }
  });

  it('lists the company projects in the selector', async () => {
    const { fixture } = await setup();

    const options = Array.from(
      fixture.nativeElement.querySelectorAll('option') as NodeListOf<HTMLOptionElement>
    ).map(o => o.textContent?.trim());

    expect(options.some(o => o?.includes('Apollo'))).toBe(true);
    expect(options.some(o => o?.includes('Borealis'))).toBe(true);
  });

  it('asks for nothing and shows no repositories until a project is chosen', async () => {
    const { fixture, http } = await setup();

    http.expectNone(() => true);
    expect(rowButton(fixture, 'acme-corp/api')).toBeNull();
  });

  it('fetches the links of the chosen project and splits them from what is still available', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);

    http
      .expectOne(r => r.method === 'GET' && r.url === `${environment.apiUrl}GitHub/projects/7/repositories`)
      .flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rowButton(fixture, 'acme-corp/web')!.textContent!.trim()).toBe('Unlink');
    expect(rowButton(fixture, 'acme-corp/api')!.textContent!.trim()).toBe('Link');
  });

  it('links a repository by its local repository id', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    rowButton(fixture, 'acme-corp/api')!.click();

    const req = http.expectOne(
      r => r.method === 'POST' && r.url === `${environment.apiUrl}GitHub/projects/7/repositories`
    );
    // The local GitHubRepository PK, not the GitHub-issued repositoryId (100).
    expect(req.request.body).toEqual({ repositoryId: 1 });

    req.flush({
      id: 11,
      projectId: 7,
      gitHubRepositoryId: 1,
      fullName: 'acme-corp/api',
      linkedByUserId: 'u1',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rowButton(fixture, 'acme-corp/api')!.textContent!.trim()).toBe('Unlink');
  });

  it('unlinks a repository on the repository-id route', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    rowButton(fixture, 'acme-corp/web')!.click();

    // FromResult(Result) returns Ok() with no body.
    http
      .expectOne(
        r =>
          r.method === 'DELETE' &&
          r.url === `${environment.apiUrl}GitHub/projects/7/repositories/2`
      )
      .flush(null);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rowButton(fixture, 'acme-corp/web')!.textContent!.trim()).toBe('Link');
  });

  it('reports links whose repository is no longer available from the installation', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http
      .expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`)
      .flush({ links: webLinked.links, unavailableCount: 2 } satisfies ProjectRepositoriesDto);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      '2 linked repositories are no longer available from this installation'
    );
  });

  it('says nothing about unavailable repositories when the count is zero', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('no longer available');
  });

  it('shows the API error when the links cannot be read', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http
      .expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`)
      .flush(apiError("'Project' was not found."), { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain("'Project' was not found.");
    // A failed read is unknown, not "nothing is linked" — it must not claim a verdict.
    expect(fixture.nativeElement.textContent).not.toContain('No repositories are linked');
  });

  it('shows the API error and keeps the repository available when linking fails', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    rowButton(fixture, 'acme-corp/api')!.click();

    http
      .expectOne(r => r.method === 'POST')
      .flush(apiError("'GitHubRepository' was not found."), {
        status: 404,
        statusText: 'Not Found',
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain("'GitHubRepository' was not found.");
    expect(rowButton(fixture, 'acme-corp/api')!.disabled).toBe(false);
  });

  /**
   * Switching project mid-mutation would let the in-flight response repopulate the newly chosen
   * project's list with the previous project's link. The disable is what closes that window.
   */
  it('locks the project selector while a mutation is in flight', async () => {
    const { fixture, http } = await setup();

    await chooseProject(fixture, 7);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(projectSelect(fixture).disabled).toBe(false);

    rowButton(fixture, 'acme-corp/api')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(projectSelect(fixture).disabled).toBe(true);

    http.expectOne(r => r.method === 'POST').flush({
      id: 11,
      projectId: 7,
      gitHubRepositoryId: 1,
      fullName: 'acme-corp/api',
      linkedByUserId: 'u1',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(projectSelect(fixture).disabled).toBe(false);
  });

  it('re-runs the project load from its own error banner', async () => {
    const { fixture, http } = await setup(connected, { projects: 'error' });

    expect(fixture.nativeElement.textContent).toContain('The projects could not be read.');

    retryButton(fixture)!.click();

    http.expectOne(`${environment.apiUrl}Projects/`).flush(projects);
    await fixture.whenStable();
    fixture.detectChanges();

    const options = Array.from(
      fixture.nativeElement.querySelectorAll('option') as NodeListOf<HTMLOptionElement>
    ).map(o => o.textContent?.trim());

    expect(options.some(o => o?.includes('Apollo'))).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('The projects could not be read.');
  });

  it('hides Link and Unlink from non-Company roles, matching Connect and Disconnect', async () => {
    const { fixture, http } = await setup(connected, { role: 'User' });

    await chooseProject(fixture, 7);
    http.expectOne(`${environment.apiUrl}GitHub/projects/7/repositories`).flush(webLinked);
    await fixture.whenStable();
    fixture.detectChanges();

    // The rows still render — it is the mutating controls that are gated.
    expect(fixture.nativeElement.textContent).toContain('acme-corp/web');
    expect(rowButton(fixture, 'acme-corp/web')).toBeNull();
    expect(rowButton(fixture, 'acme-corp/api')).toBeNull();
  });
});
