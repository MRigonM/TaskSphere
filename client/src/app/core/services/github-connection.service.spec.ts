import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { GitHubConnectionService } from './github-connection.service';
import { environment } from '../../../environments/environment';
import { GitHubConnectionDto } from '../models/github.models';

const refreshed: GitHubConnectionDto = {
  installation: {
    id: 1,
    installationId: 42,
    accountLogin: 'acme-corp',
    accountType: 'Organization',
    repositorySelection: 0,
    isSuspended: false,
  },
  repositories: [
    { id: 9, repositoryId: 900, fullName: 'acme-corp/new', defaultBranch: 'main', isPrivate: false },
  ],
};

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  return {
    service: TestBed.inject(GitHubConnectionService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('GitHubConnectionService.refreshRepositories', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('posts to the sync route and stores the refreshed connection', () => {
    const { service, http } = setup();

    service.refreshRepositories().subscribe();

    const req = http.expectOne(`${environment.apiUrl}GitHub/repositories/sync`);
    expect(req.request.method).toBe('POST');
    // Nothing is sent: the server resolves the installation from the authenticated company.
    expect(req.request.body).toEqual({});
    req.flush(refreshed);

    expect(service.connection()?.repositories.length).toBe(1);
    expect(service.connection()?.repositories[0].fullName).toBe('acme-corp/new');
    http.verify();
  });

  it('leaves the previously loaded connection alone when the refresh fails', () => {
    const { service, http } = setup();

    // Prime it the way a successful load would.
    service.loadConnection().subscribe();
    http.expectOne(`${environment.apiUrl}GitHub/connection`).flush(refreshed);

    service.refreshRepositories().subscribe({ error: () => {} });
    http
      .expectOne(`${environment.apiUrl}GitHub/repositories/sync`)
      .flush({ message: 'GitHub returned 502.' }, { status: 502, statusText: 'Bad Gateway' });

    // A failed refresh must not blank the list — the repositories are still real.
    expect(service.connection()?.repositories.length).toBe(1);
    http.verify();
  });
});
