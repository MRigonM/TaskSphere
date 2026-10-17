import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { MyClaudeComponent } from './my-claude.component';
import { environment } from '../../../environments/environment';

const base = `${environment.apiUrl}agents/me`;

function setup(status: object) {
  TestBed.configureTestingModule({
    imports: [MyClaudeComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(MyClaudeComponent);
  fixture.detectChanges();
  http.expectOne(base).flush(status);
  fixture.detectChanges();
  return { fixture, http, component: fixture.componentInstance };
}

const withAgent = (token: unknown = null) => ({ hasAgent: true, agentName: "Rigon's Claude", token });

describe('MyClaudeComponent', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  it('offers to create a Claude when there is none', () => {
    const { fixture, http, component } = setup({ hasAgent: false, agentName: null, token: null });
    expect(fixture.nativeElement.textContent).toContain('Create my Claude');

    component.create();
    http.expectOne({ method: 'POST', url: base }).flush({ id: 'a', name: "Rigon's Claude" });
    http.expectOne(base).flush(withAgent());
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain("Rigon's Claude");
  });

  it('shows a generated token once, with the claude mcp add command filled in', () => {
    const { fixture, http, component } = setup(withAgent());

    component.regenerate();
    http.expectOne({ method: 'POST', url: `${base}/token` })
      .flush({ token: 'tsk_secret', prefix: 'tsk_secretxx', createdAtUtc: '2026-10-16T12:00:00Z' });
    http.expectOne(base).flush(withAgent({ prefix: 'tsk_secretxx', createdAtUtc: '2026-10-16T12:00:00Z', lastUsedAtUtc: null }));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('tsk_secret');
    expect(text).toContain('claude mcp add --transport http tasksphere http://localhost:5000/mcp --header "Authorization: Bearer tsk_secret"');
  });

  it('asks before replacing a live token, and does nothing if declined', () => {
    const { http, component } = setup(withAgent({ prefix: 'tsk_abcdefgh', createdAtUtc: '2026-10-16T12:00:00Z', lastUsedAtUtc: null }));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);

    component.regenerate();

    expect(confirm).toHaveBeenCalled();
    http.expectNone(`${base}/token`);
  });

  it('never shows a token it did not just issue', () => {
    const { fixture } = setup(withAgent({ prefix: 'tsk_abcdefgh', createdAtUtc: '2026-10-16T12:00:00Z', lastUsedAtUtc: null }));

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('tsk_abcdefgh');
    expect(text).not.toContain('claude mcp add');
  });
});
