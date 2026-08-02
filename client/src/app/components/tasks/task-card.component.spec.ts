import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { TaskCardComponent } from './task-card.component';
import { TaskDto } from '../../core/models/tasks.models';

function makeTask(overrides: Partial<TaskDto> = {}): TaskDto {
  return {
    id: 7,
    title: 'Fix the audit chart',
    status: 'Open',
    createdAtUtc: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

describe('TaskCardComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskCardComponent],
    }).compileComponents();
  });

  it('renders the task key when present', () => {
    const fixture = TestBed.createComponent(TaskCardComponent);
    fixture.componentInstance.task = makeTask({ key: 'TS-42' });
    fixture.componentInstance.statuses = ['Open', 'Done'];
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('TS-42');
  });

  it('falls back to #id when the key is null', () => {
    const fixture = TestBed.createComponent(TaskCardComponent);
    fixture.componentInstance.task = makeTask({ key: null });
    fixture.componentInstance.statuses = ['Open', 'Done'];
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('#7');
  });

  it('falls back to #id when the key is absent', () => {
    const fixture = TestBed.createComponent(TaskCardComponent);
    fixture.componentInstance.task = makeTask();
    fixture.componentInstance.statuses = ['Open', 'Done'];
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('#7');
  });
});
