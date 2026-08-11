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

function filterSelect(fixture: { nativeElement: HTMLElement }): HTMLSelectElement {
  return fixture.nativeElement.querySelector('select') as HTMLSelectElement;
}

async function filterBy(fixture: any, value: string) {
  const select = filterSelect(fixture);
  select.value = value;
  select.dispatchEvent(new Event('change'));
  await fixture.whenStable();
  fixture.detectChanges();
}

/**
 * The name is read from its own span, not split out of the row's text: Angular drops the
 * whitespace-only node between the name and the "not linked" badge, so an unlinked row's
 * textContent runs them together as "acme-corp/docsnot linked".
 */
function repositoryNames(fixture: { nativeElement: HTMLElement }): string[] {
  return Array.from(fixture.nativeElement.querySelectorAll('li[data-repository]')).map(li =>
    (li.querySelector('span')?.textContent ?? '').trim()
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
    expect(fixture.nativeElement.textContent).not.toContain(
      'No repositories are linked to this project yet.'
    );
  });

  it('lists the company projects as filter options, with All projects first', async () => {
    const { fixture } = await setup();

    const options = Array.from(
      fixture.nativeElement.querySelectorAll('option') as NodeListOf<HTMLOptionElement>
    ).map(o => o.textContent?.trim());

    expect(options[0]).toBe('All projects');
    expect(options.some(o => o?.includes('Apollo'))).toBe(true);
    expect(options.some(o => o?.includes('Borealis'))).toBe(true);
  });

  /** Without it a screen reader announces only "combo box, All projects" — no hint of what it filters. */
  it('gives the filter an accessible name', async () => {
    const { fixture } = await setup();

    expect(filterSelect(fixture).getAttribute('aria-label')).toBe('Filter by project');
  });

  it('narrows the table to the repositories linked to the chosen project', async () => {
    const { fixture } = await setup();

    await filterBy(fixture, '8');

    // Only acme-corp/api is linked to Borealis.
    expect(repositoryNames(fixture)).toEqual(['acme-corp/api']);
  });

  /**
   * Filtering must not narrow the chips. Hiding APO while filtered to BOR would rebuild, inside
   * the new table, exactly the misreading the new table exists to remove.
   */
  it('keeps every chip on a filtered row, not just the filtered project', async () => {
    const { fixture } = await setup();

    await filterBy(fixture, '8');

    expect(chipKeys(fixture, 'acme-corp/api')).toEqual(['APO', 'BOR']);
  });

  it('restores the full table when All projects is chosen again', async () => {
    const { fixture } = await setup();

    await filterBy(fixture, '8');
    await filterBy(fixture, '');

    expect(repositoryNames(fixture)).toEqual([
      'acme-corp/api',
      'acme-corp/web',
      'acme-corp/docs',
    ]);
  });

  /**
   * The selection has to survive a re-render, not just the change event that set it: Tasks 9 and
   * 10 call load() after every mutation, and on 2026-08-09 a select whose selected state was not
   * bound rebuilt its options and silently dropped the choice.
   */
  it('keeps the chosen project selected in the control when the table refetches', async () => {
    const { fixture, http } = await setup();
    await filterBy(fixture, '8');

    // Fresh project objects, as a real read returns: *ngFor rebuilds the options, which is the
    // moment a selection living only in the DOM is lost.
    fixture.componentInstance.retry();
    http.expectOne(`${environment.apiUrl}Projects/`).flush(projects.map(p => ({ ...p })));
    http.expectOne(`${environment.apiUrl}GitHub/links`).flush(links);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(filterSelect(fixture).value).toBe('8');
  });

  it('explains an empty filtered table instead of leaving it blank', async () => {
    const { fixture } = await setup({
      repositories: [{ id: 3, fullName: 'acme-corp/docs', projects: [] }],
      unavailable: [],
    });

    await filterBy(fixture, '7');

    expect(fixture.nativeElement.textContent).toContain(
      'No repositories are linked to this project yet.'
    );
  });

  /**
   * The nastiest empty state: the project HAS links, every one of them points at a repository
   * dropped from the installation. "Nothing linked" would be a lie of exactly the kind this
   * screen was built to stop telling.
   */
  it('does not claim nothing is linked when the only links are to dropped repositories', async () => {
    const { fixture } = await setup({
      repositories: [{ id: 3, fullName: 'acme-corp/docs', projects: [] }],
      unavailable: [{ projectId: 7, projectKey: 'APO', count: 2 }],
    });

    await filterBy(fixture, '7');

    expect(fixture.nativeElement.textContent).not.toContain(
      'No repositories are linked to this project yet.'
    );
    expect(fixture.nativeElement.textContent).toContain(
      'Every repository linked to this project has been dropped from the installation'
    );
  });

  it("does not blame the filtered project for another project's dropped links", async () => {
    const { fixture } = await setup({
      repositories: [{ id: 3, fullName: 'acme-corp/docs', projects: [] }],
      unavailable: [{ projectId: 8, projectKey: 'BOR', count: 2 }],
    });

    await filterBy(fixture, '7');

    expect(fixture.nativeElement.textContent).toContain(
      'No repositories are linked to this project yet.'
    );
    expect(fixture.nativeElement.textContent).not.toContain(
      'has been dropped from the installation'
    );
  });
});
