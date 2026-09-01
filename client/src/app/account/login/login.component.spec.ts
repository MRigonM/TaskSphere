import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { LoginComponent } from './login.component';
import { environment } from '../../../environments/environment';

function setup() {
  TestBed.configureTestingModule({
    imports: [LoginComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(LoginComponent);
  fixture.detectChanges();

  return { fixture, http };
}

function submitLogin(fixture: any, email: string, password: string) {
  const component = fixture.componentInstance;
  component.form.setValue({ email, password });
  fixture.detectChanges();
  component.submit();
}

describe('LoginComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('offers a resend only when login failed because the address is unconfirmed', () => {
    const { fixture, http } = setup();

    submitLogin(fixture, 'user@example.com', 'Str0ng!Password');
    http.expectOne(`${environment.apiUrl}Account/Login`).flush(
      [{ code: 'Auth.EmailNotConfirmed', description: 'Confirm your email address before logging in.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Resend verification');
  });

  it('offers no resend for an ordinary bad password', () => {
    const { fixture, http } = setup();

    submitLogin(fixture, 'user@example.com', 'wrong');
    http.expectOne(`${environment.apiUrl}Account/Login`).flush(
      [{ code: 'General.Error', description: 'Invalid email or password.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    // Showing it here would tell anyone with a wrong password whether the address exists.
    expect(fixture.nativeElement.textContent).not.toContain('Resend verification');
  });

  it('posts the address when resend is pressed', () => {
    const { fixture, http } = setup();

    submitLogin(fixture, 'user@example.com', 'Str0ng!Password');
    http.expectOne(`${environment.apiUrl}Account/Login`).flush(
      [{ code: 'Auth.EmailNotConfirmed', description: 'Confirm your email address before logging in.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="resend-verification"]').click();

    const req = http.expectOne(`${environment.apiUrl}Account/ResendVerification`);
    expect(req.request.body).toEqual({ email: 'user@example.com' });
    req.flush('If that address has an account awaiting verification, a link is on its way.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('on its way');
  });
});
