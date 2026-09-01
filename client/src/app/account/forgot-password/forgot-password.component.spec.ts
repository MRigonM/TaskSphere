import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { ForgotPasswordComponent } from './forgot-password.component';
import { environment } from '../../../environments/environment';

function setup() {
  TestBed.configureTestingModule({
    imports: [ForgotPasswordComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(ForgotPasswordComponent);
  fixture.detectChanges();

  return { fixture, http, component: fixture.componentInstance };
}

describe('ForgotPasswordComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('posts the address and shows the neutral answer', () => {
    const { fixture, http, component } = setup();

    component.form.setValue({ email: 'user@example.com' });
    fixture.detectChanges();
    component.submit();

    const req = http.expectOne(`${environment.apiUrl}Account/ForgotPassword`);
    expect(req.request.body).toEqual({ email: 'user@example.com' });

    req.flush('If that address has an account, a password reset link is on its way.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('on its way');
  });

  it('shows the same answer when the request fails', () => {
    const { fixture, http, component } = setup();

    component.form.setValue({ email: 'user@example.com' });
    fixture.detectChanges();
    component.submit();

    http.expectOne(`${environment.apiUrl}Account/ForgotPassword`).flush(
      [{ code: 'General.Error', description: 'Something went wrong.' }],
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    // The server is careful never to reveal whether the address exists; a client that renders
    // a distinguishable error would give away what the server withheld.
    expect(fixture.nativeElement.textContent).toContain('on its way');
  });
});
