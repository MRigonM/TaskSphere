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
