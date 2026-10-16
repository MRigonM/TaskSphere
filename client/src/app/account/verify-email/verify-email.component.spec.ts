import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { VerifyEmailComponent } from './verify-email.component';
import { environment } from '../../../environments/environment';

function setup(queryParams: Record<string, string>) {
  TestBed.configureTestingModule({
    imports: [VerifyEmailComponent],
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
  const fixture = TestBed.createComponent(VerifyEmailComponent);
  fixture.detectChanges();

  return { fixture, http };
}

describe('VerifyEmailComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('posts the address and token from the link', () => {
    const { fixture, http } = setup({ email: 'user@example.com', token: 'the-token' });

    const req = http.expectOne(`${environment.apiUrl}Account/VerifyEmail`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'user@example.com', token: 'the-token' });

    req.flush('Your email address is confirmed. You can log in.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('confirmed');
  });

  it('explains an expired link instead of a blank screen', () => {
    const { fixture, http } = setup({ email: 'user@example.com', token: 'stale' });

    http.expectOne(`${environment.apiUrl}Account/VerifyEmail`).flush(
      [{ code: 'Auth.TokenInvalid', description: 'This link is no longer valid — request a new one.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('no longer valid');
  });

  it('asks for nothing when the link is missing its parameters', () => {
    const { fixture, http } = setup({});

    // A bare /account/verify-email is not a verification attempt; posting an empty token would
    // earn a 400 that says nothing useful.
    http.expectNone(`${environment.apiUrl}Account/VerifyEmail`);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('link');
  });
});
