import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { GitHubConnectionComponent } from './github-connection.component';
import { GitHubConnectionService } from '../../core/services/github-connection.service';
import { environment } from '../../../environments/environment';
import { GitHubConnectionDto, RepositorySelection } from '../../core/models/github.models';

const notConnected: GitHubConnectionDto = { installation: null, repositories: [] };

const connected: GitHubConnectionDto = {
  installation: {
    id: 1,
    installationId: 42,
    accountLogin: 'acme-corp',
    accountType: 'Organization',
    repositorySelection: 0,
    isSuspended: false,
  },
  repositories: [],
};

const connectedWithRepos: GitHubConnectionDto = {
  installation: connected.installation,
  repositories: [
    { id: 1, repositoryId: 100, fullName: 'acme-corp/api', defaultBranch: 'main', isPrivate: true },
    { id: 2, repositoryId: 101, fullName: 'acme-corp/web', defaultBranch: 'develop', isPrivate: false },
  ],
};

/**
 * `seed` primes the root service the way an earlier successful load would, so a later failure
 * can be checked for leaking the previous tenant's connection.
 */
function setup(role: string, seed?: GitHubConnectionDto) {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({ token: 'a.b.c', name: 'Rigon', role, companyId: 1, userId: 'u1' })
  );

  TestBed.configureTestingModule({
    imports: [GitHubConnectionComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });

  const http = TestBed.inject(HttpTestingController);

  if (seed) {
    TestBed.inject(GitHubConnectionService).loadConnection().subscribe();
    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(seed);
  }

  const fixture = TestBed.createComponent(GitHubConnectionComponent);
  fixture.detectChanges();

  return { fixture, http };
}

/** The shape ApiBaseController.MapErrors actually returns: a list of { code, description }. */
function apiError(description: string) {
  return [{ code: 'GitHub.Failed', description }];
}

function connectButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement | null {
  return (
    Array.from(fixture.nativeElement.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Connect GitHub')
    ) ?? null
  );
}

function disconnectButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement | null {
  return (
    Array.from(fixture.nativeElement.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Disconnect')
    ) ?? null
  );
}

function retryButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement | null {
  return (
    Array.from(fixture.nativeElement.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Try again')
    ) ?? null
  );
}

/**
 * The connected branch renders <app-github-repository-links>, which reads the company's projects
 * and the company-wide link table on init. Drained here so the outstanding requests do not fail
 * verify(); its own behaviour is covered in github-repository-links.component.spec.ts.
 */
function flushLinksTable(http: HttpTestingController) {
  http.expectOne(`${environment.apiUrl}Projects/`).flush([]);
  http.expectOne(`${environment.apiUrl}GitHub/links`).flush({ repositories: [], unavailable: [] });
}

describe('GitHubConnectionComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      localStorage.removeItem('tasksphere_auth');
      vi.restoreAllMocks();
    }
  });

  it('renders the Connect button when no installation is mapped', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(notConnected);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(connectButton(fixture)).not.toBeNull();
  });

  it('hides the Connect button and names the account once an installation is mapped', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connected);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    expect(connectButton(fixture)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('acme-corp');
  });

  it('hides the Connect button from non-Company roles', async () => {
    const { fixture, http } = setup('User');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(notConnected);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(connectButton(fixture)).toBeNull();
  });

  it('fetches the install URL and sends the browser there when Connect is clicked', async () => {
    const { fixture, http } = setup('Company');
    const redirect = vi
      .spyOn(TestBed.inject(GitHubConnectionService), 'redirectTo')
      .mockImplementation(() => {});

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(notConnected);
    await fixture.whenStable();
    fixture.detectChanges();

    connectButton(fixture)!.click();

    http
      .expectOne(`${environment.apiUrl}GitHub/install`)
      .flush({ url: 'https://github.com/apps/tasksphere/installations/new?state=xyz' });
    await fixture.whenStable();

    expect(redirect).toHaveBeenCalledWith(
      'https://github.com/apps/tasksphere/installations/new?state=xyz'
    );
  });

  it('shows the API error and drops stale connection data when the load fails', async () => {
    const { fixture, http } = setup('Company', connected);

    http
      .expectOne(`${environment.apiUrl}GitHub/connection`)
      .flush(apiError('The GitHub connection could not be read.'), {
        status: 400,
        statusText: 'Bad Request',
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'The GitHub connection could not be read.'
    );
    expect(fixture.nativeElement.textContent).not.toContain('acme-corp');
    // A failed load is "unknown", not "not connected" — it must not assert either verdict.
    expect(fixture.nativeElement.textContent).not.toContain('No GitHub organization is connected');
    expect(connectButton(fixture)).toBeNull();
  });

  it('shows the API error and re-enables Connect when the install URL cannot be fetched', async () => {
    const { fixture, http } = setup('Company');
    const redirect = vi
      .spyOn(TestBed.inject(GitHubConnectionService), 'redirectTo')
      .mockImplementation(() => {});

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(notConnected);
    await fixture.whenStable();
    fixture.detectChanges();

    connectButton(fixture)!.click();

    http
      .expectOne(`${environment.apiUrl}GitHub/install`)
      .flush(apiError('The GitHub App is not configured.'), {
        status: 400,
        statusText: 'Bad Request',
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(redirect).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('The GitHub App is not configured.');
    expect(connectButton(fixture)!.disabled).toBe(false);
  });

  it('renders the empty state when the load succeeds with no installation', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(notConnected);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No GitHub organization is connected yet.');
    expect(disconnectButton(fixture)).toBeNull();
  });

  it('lists the repositories of the connected installation', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connectedWithRepos);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('acme-corp/api');
    expect(text).toContain('acme-corp/web');
    expect(text).toContain('develop');
  });

  it('says a repository-selection of All refreshes on demand rather than live', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush({
      ...connectedWithRepos,
      installation: {
        ...connectedWithRepos.installation!,
        repositorySelection: RepositorySelection.All,
      },
    } satisfies GitHubConnectionDto);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    expect(fixture.nativeElement.textContent).toContain(
      'This list refreshes on demand, so repositories added on GitHub since the last refresh are not shown yet.'
    );
  });

  it('states that disconnecting leaves the App installed on GitHub and that reconnecting restores it', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connected);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('does not uninstall the TaskSphere App on GitHub');
    expect(text).toContain('Reconnecting restores the same installation.');
  });

  it('disconnects and returns the screen to the not-connected state', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connectedWithRepos);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    disconnectButton(fixture)!.click();

    const req = http.expectOne(
      r => r.method === 'DELETE' && r.url === `${environment.apiUrl}GitHub/connection`
    );
    // FromResult(Result) returns Ok() with no body.
    req.flush(null);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).not.toContain('acme-corp/api');
    expect(text).toContain('No GitHub organization is connected yet.');
    expect(connectButton(fixture)).not.toBeNull();
  });

  it('shows the API error and keeps the connection when disconnecting fails', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connectedWithRepos);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    disconnectButton(fixture)!.click();

    http
      .expectOne(r => r.method === 'DELETE' && r.url === `${environment.apiUrl}GitHub/connection`)
      .flush(apiError('No GitHub organization is connected.'), {
        status: 400,
        statusText: 'Bad Request',
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No GitHub organization is connected.');
    expect(fixture.nativeElement.textContent).toContain('acme-corp/api');
    expect(disconnectButton(fixture)!.disabled).toBe(false);
    // Try again only exists to recover a load that hid the panel; the panel is still here.
    expect(retryButton(fixture)).toBeNull();
  });

  it('hides the Disconnect button from non-Company roles', async () => {
    const { fixture, http } = setup('User');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connected);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    // Without this the test would also pass if the connected branch failed to render at all.
    expect(fixture.nativeElement.textContent).toContain('acme-corp');
    expect(disconnectButton(fixture)).toBeNull();
  });

  /**
   * Forward-looking. B1 has no webhooks, so nothing in it can ever set `IsSuspended` — only the
   * `installation.suspend` webhook does, and that is B2's. The flag is seeded straight into the
   * response here to pin the display; B2 owns producing the state.
   */
  it('warns when a seeded installation is suspended (B2 produces this state, not B1)', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush({
      ...connected,
      installation: { ...connected.installation!, isSuspended: true },
    } satisfies GitHubConnectionDto);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    expect(fixture.nativeElement.textContent).toContain('suspended on GitHub');
  });

  it('re-runs the load from the error banner when the first attempt failed', async () => {
    const { fixture, http } = setup('Company');

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(apiError('Upstream is down.'), {
      status: 500,
      statusText: 'Server Error',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    retryButton(fixture)!.click();

    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(connected);
    await fixture.whenStable();
    fixture.detectChanges();
    flushLinksTable(http);

    expect(fixture.nativeElement.textContent).toContain('acme-corp');
    expect(fixture.nativeElement.textContent).not.toContain('Upstream is down.');
  });
});
