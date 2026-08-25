import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { environment } from '../../../environments/environment';
import { ProjectsApiService } from './projects.service';

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  return {
    service: TestBed.inject(ProjectsApiService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('ProjectsApiService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('sends the toggle to the settings endpoint as a PATCH', () => {
    const { service, http } = setup();

    service.updateSettings(7, true).subscribe();

    const req = http.expectOne(`${environment.apiUrl}Projects/7/settings`);
    expect(req.request.method).toBe('PATCH');
    // The body carries the toggle and nothing else: the server DTO has one member, and a
    // wider body here would be the first half of widening it there.
    expect(req.request.body).toEqual({ autoDoneOnMerge: true });

    req.flush({ id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: true });
  });

  it('sends false when the toggle is turned off', () => {
    const { service, http } = setup();

    service.updateSettings(7, false).subscribe();

    const req = http.expectOne(`${environment.apiUrl}Projects/7/settings`);
    expect(req.request.body).toEqual({ autoDoneOnMerge: false });

    req.flush({ id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: false });
  });

  it('posts a refresh for one project', () => {
    const { service, http } = setup();

    service.refreshGitHub(7).subscribe();

    const req = http.expectOne(`${environment.apiUrl}Projects/7/github-refresh`);
    expect(req.request.method).toBe('POST');
    // No body: the project id is the whole request.
    expect(req.request.body).toEqual({});

    req.flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 1 });
  });
});
