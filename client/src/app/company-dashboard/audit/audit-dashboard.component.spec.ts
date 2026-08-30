import { describe, it, expect, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { AuditDashboardComponent } from './audit-dashboard.component';
import { AuditService } from '../../core/services/audit.service';
import { AuditLogDto } from '../../core/models/audit.models';

/** What AuditAttribute writes: a real request, with every HTTP field filled in. */
const fromARequest: AuditLogDto = {
  id: 1,
  timestamp: '2026-08-25T08:00:00Z',
  username: 'rigon',
  httpMethod: 'POST',
  path: '/api/GitHub/activity/sync',
  ip: '127.0.0.1',
  action: 'Synced GitHub activity',
  requestData: null,
  statusCode: 200,
  durationMs: 431,
};

/**
 * What the merge → Done transition writes — the first audit entry in this app that never came
 * from a request. Method, path and IP are empty, and status and duration are zero.
 */
const fromInsideTheApp: AuditLogDto = {
  id: 2,
  timestamp: '2026-08-25T08:00:01Z',
  username: 'rigon',
  httpMethod: '',
  path: '',
  ip: null,
  action: 'Moved TS-42 to Done — pull request #7 was merged',
  requestData: null,
  statusCode: 0,
  durationMs: 0,
};

function setup(logs: AuditLogDto[]) {
  TestBed.configureTestingModule({
    imports: [AuditDashboardComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: AuditService,
        useValue: {
          getPaged: vi.fn().mockReturnValue(
            of({ items: logs, total: logs.length, page: 1, pageSize: 20 }),
          ),
          getStats: vi.fn().mockReturnValue(
            of({ totalRequests: 0, activeUsers: 0, topEndpoints: [], requestsPerDay: [] }),
          ),
        },
      },
    ],
  });

  const fixture: ComponentFixture<AuditDashboardComponent> =
    TestBed.createComponent(AuditDashboardComponent);
  fixture.detectChanges();

  return fixture;
}

function rowText(fixture: ComponentFixture<AuditDashboardComponent>, index: number): string {
  return fixture.nativeElement.querySelectorAll('tbody tr')[index].textContent ?? '';
}

describe('AuditDashboardComponent — an entry with no request behind it', () => {
  it('renders the entry without throwing, and names the action', () => {
    const fixture = setup([fromInsideTheApp]);

    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
    expect(rowText(fixture, 0)).toContain('Moved TS-42 to Done');
    expect(rowText(fixture, 0)).toContain('rigon');
  });

  it('shows no status rather than a red zero', () => {
    const fixture = setup([fromInsideTheApp]);

    // statusClass(0) falls through to the red branch, so rendering the zero would paint a
    // successful transition as a failure.
    expect(fixture.nativeElement.querySelector('[data-testid="no-status"]')).toBeTruthy();
    expect(rowText(fixture, 0)).not.toContain('0ms');
  });

  it('still renders a real request entry with its status and duration', () => {
    const fixture = setup([fromARequest]);

    expect(fixture.nativeElement.querySelector('[data-testid="no-status"]')).toBeNull();
    expect(rowText(fixture, 0)).toContain('200');
    expect(rowText(fixture, 0)).toContain('431ms');
  });
});
