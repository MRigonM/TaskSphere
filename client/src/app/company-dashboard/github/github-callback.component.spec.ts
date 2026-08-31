import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';

import { GitHubCallbackComponent } from './github-callback.component';
import { environment } from '../../../environments/environment';
import { GitHubConnectionDto } from '../../core/models/github.models';

function setup(queryParams: Record<string, string>) {
  TestBed.configureTestingModule({
    imports: [GitHubCallbackComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
      },
    ],
  });

  const http = TestBed.inject(HttpTestingController);
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(GitHubCallbackComponent);
  fixture.detectChanges();

  return { fixture, http, navigate };
}

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

describe('GitHubCallbackComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('posts installationId, state and code, then routes to the connection screen', async () => {
    const { fixture, http, navigate } = setup({
      installation_id: '42',
      state: 'the-state',
      code: 'the-code',
      setup_action: 'install',
    });

    const req = http.expectOne(`${environment.apiUrl}GitHub/callback`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      installationId: 42,
      state: 'the-state',
      code: 'the-code',
    });

    req.flush(connected);
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/dashboard/github');
  });

  it('re-syncs the repositories when GitHub redirects back from an installation update', async () => {
    // What GitHub actually sends when the App's "Redirect on update" is on and the user changes
    // repository access: an installation_id and a code, but NO state -- it was never our install
    // flow, so there is no state to mint. Judged by the install path's rules this reads as a
    // failed authorization, which is what it did in production on 2026-08-31.
    const { fixture, http, navigate } = setup({
      installation_id: '152473756',
      code: '320378c0ff437fa6d26b',
      setup_action: 'update',
    });

    http.expectNone(`${environment.apiUrl}GitHub/callback`);

    // Nothing from the URL is used: the sync endpoint resolves the installation from the
    // authenticated company, so a forged installation_id cannot reach another tenant.
    const req = http.expectOne(`${environment.apiUrl}GitHub/repositories/sync`);
    expect(req.request.method).toBe('POST');

    req.flush(connected);
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/dashboard/github');
    expect(fixture.nativeElement.textContent).not.toContain('could not be completed');
  });

  it('explains a failed refresh after an update instead of claiming the authorization failed', async () => {
    const { fixture, http, navigate } = setup({
      installation_id: '152473756',
      code: 'the-code',
      setup_action: 'update',
    });

    http.expectOne(`${environment.apiUrl}GitHub/repositories/sync`).flush(
      [{ code: 'GitHub.NotConnected', description: 'This company is not connected to GitHub.' }],
      { status: 400, statusText: 'Bad Request' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('This company is not connected to GitHub.');
  });

  it('shows an error and does not call the API when the authorization code is missing', async () => {
    const { fixture, http, navigate } = setup({
      installation_id: '42',
      setup_action: 'install',
    });

    http.expectNone(`${environment.apiUrl}GitHub/callback`);
    fixture.detectChanges();

    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('could not be completed');
  });

  it('shows an error and stays put when the API rejects the callback', async () => {
    const { fixture, http, navigate } = setup({
      installation_id: '42',
      state: 'stale-state',
      code: 'the-code',
    });

    // The real failure body: ApiBaseController.MapErrors returns IReadOnlyList<Error>,
    // where Error is record Error(string Code, string Description).
    http.expectOne(`${environment.apiUrl}GitHub/callback`).flush(
      [{ code: 'GitHub.StateInvalid', description: 'The install state has expired.' }],
      { status: 400, statusText: 'Bad Request' }
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('The install state has expired.');
  });
});
