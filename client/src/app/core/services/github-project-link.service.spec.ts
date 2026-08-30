import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GitHubProjectLinkService } from './github-project-link.service';
import { environment } from '../../../environments/environment';
import { CompanyRepositoryLinksDto } from '../models/github.models';

describe('GitHubProjectLinkService', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('reads the company-wide links from GitHub/links', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    const payload: CompanyRepositoryLinksDto = {
      repositories: [{ id: 1, fullName: 'acme-corp/api', projects: [{ id: 7, key: 'APO', name: 'Apollo' }] }],
      unavailable: [],
    };

    let received: CompanyRepositoryLinksDto | undefined;
    TestBed.inject(GitHubProjectLinkService)
      .getCompanyLinks()
      .subscribe(r => (received = r));

    const req = TestBed.inject(HttpTestingController).expectOne(`${environment.apiUrl}GitHub/links`);
    expect(req.request.method).toBe('GET');
    req.flush(payload);

    expect(received).toEqual(payload);
  });
});
