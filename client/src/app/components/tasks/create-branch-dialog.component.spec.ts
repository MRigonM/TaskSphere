import { describe, it, expect, afterEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { environment } from '../../../environments/environment';
import { CreateBranchDialogComponent } from './create-branch-dialog.component';

const suggestion = {
  taskKey: 'TS-42',
  suggestedName: 'TS-42/crud-for-product',
  repositories: [{ id: 7, fullName: 'rigon-org/api', defaultBranch: 'main' }],
};

function setup(): { fixture: ComponentFixture<CreateBranchDialogComponent>; http: HttpTestingController } {
  TestBed.configureTestingModule({
    imports: [CreateBranchDialogComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const fixture = TestBed.createComponent(CreateBranchDialogComponent);
  fixture.componentRef.setInput('taskId', 42);
  fixture.detectChanges();

  return { fixture, http: TestBed.inject(HttpTestingController) };
}

function flushSuggestion(http: HttpTestingController, body: any = suggestion) {
  http.expectOne(`${environment.apiUrl}Tasks/42/github-branch/suggestion`).flush(body);
}

describe('CreateBranchDialogComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController, null)?.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });

  it('prefills the suggested name', () => {
    const { fixture, http } = setup();
    flushSuggestion(http);
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-branch-name]');
    expect(input.value).toBe('TS-42/crud-for-product');
  });

  it('hides the repository picker when the project links exactly one repository', () => {
    const { fixture, http } = setup();
    flushSuggestion(http);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-repo-select]')).toBeNull();
  });

  it('shows the repository picker when the project links several', () => {
    const { fixture, http } = setup();
    flushSuggestion(http, {
      ...suggestion,
      repositories: [
        { id: 7, fullName: 'rigon-org/api', defaultBranch: 'main' },
        { id: 8, fullName: 'rigon-org/web', defaultBranch: 'develop' },
      ],
    });
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('[data-repo-select]');
    expect(select).not.toBeNull();
    expect(select.querySelectorAll('option').length).toBe(2);
  });

  it('refuses to create once the key has been edited out of the name', () => {
    const { fixture, http } = setup();
    flushSuggestion(http);
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-branch-name]');
    input.value = 'just-a-branch';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-create]');
    expect(button.disabled).toBe(true);
    // The rule is stated, not merely enforced — a disabled button with no reason is a dead end.
    expect(fixture.nativeElement.textContent).toContain('TS-42');
  });

  it('emits the created branch and posts the chosen repository', () => {
    const { fixture, http } = setup();
    flushSuggestion(http, {
      ...suggestion,
      repositories: [
        { id: 7, fullName: 'rigon-org/api', defaultBranch: 'main' },
        { id: 8, fullName: 'rigon-org/web', defaultBranch: 'develop' },
      ],
    });
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('[data-repo-select]');
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    let emitted: any;
    fixture.componentInstance.created.subscribe((c: any) => (emitted = c));

    fixture.nativeElement.querySelector('[data-create]').click();

    const req = http.expectOne(`${environment.apiUrl}Tasks/42/github-branch`);
    expect(req.request.body).toEqual({ repositoryId: 8, name: 'TS-42/crud-for-product' });

    req.flush({
      id: 1,
      name: 'TS-42/crud-for-product',
      headSha: 'abc',
      htmlUrl: 'https://github.com/rigon-org/web/tree/TS-42/crud-for-product',
      alreadyExisted: false,
    });
    fixture.detectChanges();

    expect(emitted).toBeTruthy();
  });

  it('renders the API message when the suggestion cannot be produced', () => {
    const { fixture, http } = setup();
    http
      .expectOne(`${environment.apiUrl}Tasks/42/github-branch/suggestion`)
      .flush(
        [
          {
            code: 'GitHub.NoLinkedRepository',
            description: "No GitHub repository is linked to this task's project.",
          },
        ],
        { status: 400, statusText: 'Bad Request' }
      );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No GitHub repository is linked');
    expect(fixture.nativeElement.querySelector('[data-create]')).toBeNull();
  });

  it('clears the previous error when a create starts', () => {
    const { fixture, http } = setup();
    flushSuggestion(http);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-create]').click();
    http
      .expectOne(`${environment.apiUrl}Tasks/42/github-branch`)
      .flush([{ code: 'GitHub.RateLimited', description: 'GitHub rate limit hit.' }], { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('rate limit');

    fixture.nativeElement.querySelector('[data-create]').click();
    fixture.detectChanges();
    // Reset when the run STARTS, not only when it succeeds: otherwise the previous failure
    // stays on screen under a request that has not answered yet.
    expect(fixture.nativeElement.textContent).not.toContain('rate limit');

    http.expectOne(`${environment.apiUrl}Tasks/42/github-branch`).flush({
      id: 1, name: 'TS-42/crud-for-product', headSha: 'abc', htmlUrl: 'https://x', alreadyExisted: false,
    });
  });
});
