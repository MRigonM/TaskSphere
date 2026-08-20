import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { environment } from '../../../environments/environment';
import { GitHubActivityService } from './github-activity.service';
import { PullRequestState, TaskGitHubActivityDto } from '../models/github-activity.models';

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
    },
  ],
  branches: [],
  pullRequests: [],
  lastSyncedAtUtc: '2026-08-12T07:00:00Z',
};

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  return {
    service: TestBed.inject(GitHubActivityService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('GitHubActivityService', () => {
  afterEach(() => {
    try {
      // `null` default, not a bare inject: the enum test configures no testing module at all,
      // and a missing provider there is the expected state, not a failure.
      TestBed.inject(HttpTestingController, null)?.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('reads a task activity from the task route, not the GitHub route', () => {
    const { service, http } = setup();

    let received: TaskGitHubActivityDto | undefined;
    service.getForTask(42).subscribe(a => (received = a));

    // The read is on TasksController because that is the CompanyOrUser gate a project Member
    // can reach; the GitHub controller is Company-only.
    const req = http.expectOne(`${environment.apiUrl}Tasks/42/github-activity`);
    expect(req.request.method).toBe('GET');

    req.flush(activity);

    expect(received?.commits[0].shortSha).toBe('1234567');
    expect(received?.lastSyncedAtUtc).toBe('2026-08-12T07:00:00Z');
  });

  it('posts the sync to the company-wide GitHub route', () => {
    const { service, http } = setup();

    service.sync().subscribe();

    const req = http.expectOne(`${environment.apiUrl}GitHub/activity/sync`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();

    req.flush({
      repositoriesSynced: 2,
      commits: 5,
      branches: 3,
      pullRequests: 1,
      linksCreated: 4,
      failures: [],
    });
  });

  it('carries partial failures through as a success', () => {
    const { service, http } = setup();

    let failures: unknown;
    service.sync().subscribe(r => (failures = r.failures));

    http.expectOne(`${environment.apiUrl}GitHub/activity/sync`).flush({
      repositoriesSynced: 1,
      commits: 0,
      branches: 1,
      pullRequests: 0,
      linksCreated: 0,
      failures: [{ repositoryFullName: 'rigon-org/api', reason: 'GitHub returned 404.', branch: null }],
    });

    expect(failures).toEqual([
      { repositoryFullName: 'rigon-org/api', reason: 'GitHub returned 404.', branch: null },
    ]);
  });

  it('mirrors the server enum as numbers, because the API serializes it as an int', () => {
    expect(PullRequestState.Open).toBe(0);
    expect(PullRequestState.Closed).toBe(1);
    expect(PullRequestState.Merged).toBe(2);
  });
});
