import { describe, it, expect, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GitHubRepositoryLinksComponent } from './github-repository-links.component';
import { environment } from '../../../environments/environment';
import { CompanyRepositoryLinksDto } from '../../core/models/github.models';
import { ProjectDto } from '../../core/models/projects.models';

const projects: ProjectDto[] = [
  { id: 7, name: 'Apollo', key: 'APO' },
  { id: 8, name: 'Borealis', key: 'BOR' },
];

/** `id` is the local GitHubRepository PK — the id the link endpoints take. */
const links: CompanyRepositoryLinksDto = {
  repositories: [
    {
      id: 1,
      fullName: 'acme-corp/api',
      projects: [
        { id: 7, key: 'APO', name: 'Apollo' },
        { id: 8, key: 'BOR', name: 'Borealis' },
      ],
    },
    { id: 2, fullName: 'acme-corp/web', projects: [{ id: 7, key: 'APO', name: 'Apollo' }] },
    { id: 3, fullName: 'acme-corp/docs', projects: [] },
  ],
  unavailable: [],
};

/** The shape ApiBaseController.MapErrors actually returns: a list of { code, description }. */
function apiError(description: string) {
  return [{ code: 'GitHub.Failed', description }];
}

async function setup(
  payload: CompanyRepositoryLinksDto = links,
  options: { links?: 'ok' | 'error' } = {}
) {
  localStorage.setItem(
    'tasksphere_auth',
    JSON.stringify({ token: 'a.b.c', name: 'Rigon', role: 'Company', companyId: 1, userId: 'u1' })
  );

  TestBed.configureTestingModule({
    imports: [GitHubRepositoryLinksComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(GitHubRepositoryLinksComponent);
  fixture.detectChanges();

  http.expectOne(`${environment.apiUrl}Projects/`).flush(projects);

  const req = http.expectOne(`${environment.apiUrl}GitHub/links`);
  if (options.links === 'error') {
    req.flush(apiError('The links could not be read.'), { status: 500, statusText: 'Server Error' });
  } else {
    req.flush(payload);
  }

  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, http };
}

function row(fixture: { nativeElement: HTMLElement }, fullName: string): HTMLElement | null {
  return (
    Array.from(fixture.nativeElement.querySelectorAll('li[data-repository]')).find(li =>
      li.textContent?.includes(fullName)
    ) as HTMLElement ?? null
  );
}

function chipKeys(fixture: { nativeElement: HTMLElement }, fullName: string): string[] {
  return Array.from(row(fixture, fullName)?.querySelectorAll('[data-chip]') ?? []).map(
    c => (c as HTMLElement).dataset['chip'] as string
  );
}

describe('GitHubRepositoryLinksComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      localStorage.removeItem('tasksphere_auth');
    }
  });

  /**
   * The defect this component exists to fix: the old screen rendered one project at a time, so a
   * repository linked to two projects looked like it could only ever have one. Reported from a
   * real session on 2026-08-09 while the database held both links.
   */
  it('shows every project a repository is linked to, on one row', async () => {
    const { fixture } = await setup();

    expect(chipKeys(fixture, 'acme-corp/api')).toEqual(['APO', 'BOR']);
  });

  it('renders a repository with no links as a row saying so', async () => {
    const { fixture } = await setup();

    expect(chipKeys(fixture, 'acme-corp/docs')).toEqual([]);
    expect(row(fixture, 'acme-corp/docs')!.textContent).toContain('not linked');
  });

  it('says the installation has no repositories rather than showing an empty table', async () => {
    const { fixture } = await setup({ repositories: [], unavailable: [] });

    expect(fixture.nativeElement.textContent).toContain(
      'No repositories are available from this installation.'
    );
  });
});
