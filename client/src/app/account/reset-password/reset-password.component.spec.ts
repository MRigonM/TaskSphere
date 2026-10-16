import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { ResetPasswordComponent } from './reset-password.component';
import { environment } from '../../../environments/environment';

function setup(queryParams: Record<string, string>) {
  TestBed.configureTestingModule({
    imports: [ResetPasswordComponent],
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
  const fixture = TestBed.createComponent(ResetPasswordComponent);
  fixture.detectChanges();

  return { fixture, http, component: fixture.componentInstance };
}

describe('ResetPasswordComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('posts the new password with the address and token from the link', () => {
    const { fixture, http, component } = setup({ email: 'user@example.com', token: 'the-token' });

    component.form.setValue({ password: 'N3w!Password', confirmPassword: 'N3w!Password' });
    fixture.detectChanges();
    component.submit();

    const req = http.expectOne(`${environment.apiUrl}Account/ResetPassword`);
    expect(req.request.body).toEqual({
      email: 'user@example.com',
      token: 'the-token',
      password: 'N3w!Password',
      confirmPassword: 'N3w!Password',
    });

    req.flush('Your password has been changed. You can log in.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('You can log in');
  });

  it('refuses to post when the two passwords differ', () => {
    const { fixture, http, component } = setup({ email: 'user@example.com', token: 'the-token' });

    component.form.setValue({ password: 'N3w!Password', confirmPassword: 'Different1!' });
    fixture.detectChanges();
    component.submit();
    // The mismatch is rejected synchronously, so nothing else triggers a render pass here.
    fixture.detectChanges();

    http.expectNone(`${environment.apiUrl}Account/ResetPassword`);
    expect(fixture.nativeElement.textContent).toContain('match');
  });

  it('explains an expired link instead of a blank screen', () => {
    const { fixture, http, component } = setup({ email: 'user@example.com', token: 'stale' });

    component.form.setValue({ password: 'N3w!Password', confirmPassword: 'N3w!Password' });
    fixture.detectChanges();
    component.submit();

    http.expectOne(`${environment.apiUrl}Account/ResetPassword`).flush(
      [{ code: 'Auth.TokenInvalid', description: 'This link is no longer valid — request a new one.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('no longer valid');
  });

  it('says the link is incomplete when it carries no token', () => {
    const { fixture, http } = setup({});

    http.expectNone(`${environment.apiUrl}Account/ResetPassword`);
    expect(fixture.nativeElement.textContent).toContain('incomplete');
  });
});
