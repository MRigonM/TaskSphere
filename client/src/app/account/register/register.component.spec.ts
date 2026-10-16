import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { RegisterComponent } from './register.component';
import { environment } from '../../../environments/environment';

function setup() {
  TestBed.configureTestingModule({
    imports: [RegisterComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(RegisterComponent);
  fixture.detectChanges();
  return { fixture, http, component: fixture.componentInstance };
}

const filled = { name: 'Rigon', email: 'r@example.com', password: 'Str0ng!Pass', confirmPassword: 'Str0ng!Pass' };

describe('RegisterComponent account kind', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('defaults to a company account', () => {
    const { component, http } = setup();
    component.form.patchValue(filled);
    component.submit();

    const req = http.expectOne(`${environment.apiUrl}Account/Register`);
    expect(req.request.body.isPersonal).toBe(false);
    req.flush('ok');
  });

  it('posts isPersonal when Personal is chosen', () => {
    const { component, fixture, http } = setup();
    // Clicked, not component.choose(true): the app is zoneless, and only the click's event
    // listener marks the view dirty — a direct call leaves the heading stale.
    const personal = fixture.nativeElement.querySelector('button[role="radio"][aria-checked="false"]') as HTMLButtonElement;
    personal.click();
    fixture.detectChanges();
    component.form.patchValue(filled);
    component.submit();

    const req = http.expectOne(`${environment.apiUrl}Account/Register`);
    expect(req.request.body.isPersonal).toBe(true);
    req.flush('ok');
    expect(fixture.nativeElement.textContent).toContain('just you and your Claude');
  });
});
