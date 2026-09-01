import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { AcceptInviteComponent } from './accept-invite.component';
import { environment } from '../../../environments/environment';

function setup(queryParams: Record<string, string>) {
  TestBed.configureTestingModule({
    imports: [AcceptInviteComponent],
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
  const fixture = TestBed.createComponent(AcceptInviteComponent);
  fixture.detectChanges();

  return { fixture, http, component: fixture.componentInstance };
}

describe('AcceptInviteComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('posts the password with the address and token from the link', () => {
    const { fixture, http, component } = setup({ email: 'member@example.com', token: 'the-token' });

    component.form.setValue({ password: 'Str0ng!Password', confirmPassword: 'Str0ng!Password' });
    fixture.detectChanges();
    component.submit();

    const req = http.expectOne(`${environment.apiUrl}Account/AcceptInvite`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      email: 'member@example.com',
      token: 'the-token',
      password: 'Str0ng!Password',
      confirmPassword: 'Str0ng!Password',
    });

    req.flush('Your password is set. You can log in.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('You can log in');
  });

  it('refuses to post when the two passwords differ', () => {
    const { fixture, http, component } = setup({ email: 'member@example.com', token: 'the-token' });

    component.form.setValue({ password: 'Str0ng!Password', confirmPassword: 'Different1!' });
    fixture.detectChanges();
    component.submit();
    // The mismatch is rejected synchronously, so nothing else triggers a render pass here.
    fixture.detectChanges();

    // Caught in the browser: a round trip to be told the two boxes differ is a round trip
    // nobody needs.
    http.expectNone(`${environment.apiUrl}Account/AcceptInvite`);
    expect(fixture.nativeElement.textContent).toContain('match');
  });

  it('explains a stale link instead of a blank screen', () => {
    const { fixture, http, component } = setup({ email: 'member@example.com', token: 'stale' });

    component.form.setValue({ password: 'Str0ng!Password', confirmPassword: 'Str0ng!Password' });
    fixture.detectChanges();
    component.submit();

    http.expectOne(`${environment.apiUrl}Account/AcceptInvite`).flush(
      [{ code: 'Auth.TokenInvalid', description: 'This link is no longer valid — request a new one.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('no longer valid');
  });

  it('says the link is incomplete when it carries no token', () => {
    const { fixture, http } = setup({});

    http.expectNone(`${environment.apiUrl}Account/AcceptInvite`);
    expect(fixture.nativeElement.textContent).toContain('incomplete');
  });
});
