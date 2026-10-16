import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { UsersDashboardComponent } from './users-dashboard.component';
import { environment } from '../../../environments/environment';

function setup() {
  TestBed.configureTestingModule({
    imports: [UsersDashboardComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(UsersDashboardComponent);
  fixture.detectChanges();

  // The component loads the user list on init; that request is not what these tests are about.
  http.match(req => req.url.startsWith(`${environment.apiUrl}Account/Users`)).forEach(r => r.flush([]));
  fixture.detectChanges();

  return { fixture, http, component: fixture.componentInstance };
}

describe('UsersDashboardComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('creates a member by posting only a name and an address', () => {
    const { fixture, http, component } = setup();

    component.openCreate();
    fixture.detectChanges();
    component.userForm.patchValue({ name: 'New Member', email: 'member@example.com' });
    fixture.detectChanges();
    component.submitModal();

    const req = http.expectOne(`${environment.apiUrl}Account/CreateUser`);
    // No password fields at all: the member sets their own through the invitation link, and an
    // admin who never chooses one never has to transmit one.
    expect(req.request.body).toEqual({ name: 'New Member', email: 'member@example.com' });

    req.flush('Member added. They have been emailed a link to set their password.');
    fixture.detectChanges();

    http.match(req => req.url.startsWith(`${environment.apiUrl}Account/Users`)).forEach(r => r.flush([]));
  });

  it('shows no password inputs on the create form', () => {
    const { fixture, component } = setup();

    component.openCreate();
    fixture.detectChanges();

    const passwordInputs = fixture.nativeElement.querySelectorAll(
      'input[formControlName="password"], input[formControlName="confirmPassword"]',
    );
    expect(passwordInputs.length).toBe(0);
  });
});
