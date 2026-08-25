import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ProjectPageComponent } from './project-page.component';
import { ProjectsApiService } from './projects.service';
import { AccountApiService } from '../../core/services/account-api.service';
import { ToastService } from '../../core/services/toast.service';
import { ProjectDto } from '../../core/models/projects.models';

const project: ProjectDto = { id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: false };

function setup(current: ProjectDto = project) {
  const toast = { show: vi.fn() };

  const projectsApi = {
    getAll: vi.fn().mockReturnValue(of([current])),
    getMembers: vi.fn().mockReturnValue(of([])),
    updateSettings: vi.fn().mockReturnValue(of({ ...current, autoDoneOnMerge: true })),
  };

  TestBed.configureTestingModule({
    imports: [ProjectPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ProjectsApiService, useValue: projectsApi },
      { provide: AccountApiService, useValue: { getUsers: vi.fn().mockReturnValue(of([])) } },
      { provide: ToastService, useValue: toast },
      {
        provide: ActivatedRoute,
        useValue: { paramMap: of(new Map([['projectId', '7']]) as any) },
      },
    ],
  });

  const fixture: ComponentFixture<ProjectPageComponent> =
    TestBed.createComponent(ProjectPageComponent);
  fixture.detectChanges();

  return { fixture, projectsApi, toast };
}

function checkbox(fixture: ComponentFixture<ProjectPageComponent>): HTMLInputElement {
  return fixture.nativeElement.querySelector('[data-testid="auto-done-on-merge"]');
}

describe('ProjectPageComponent — the auto-done toggle', () => {
  let harness: ReturnType<typeof setup>;

  beforeEach(() => {
    harness = setup();
  });

  it('renders the toggle from the loaded project', () => {
    expect(checkbox(harness.fixture)).toBeTruthy();
    expect(checkbox(harness.fixture).checked).toBe(false);
  });

  it('sends the new value when the box is ticked in the template', () => {
    // Driven through the DOM, not by calling the handler: a test that calls the handler
    // directly cannot witness the (change) binding, which is exactly the gap the 2026-08-23
    // sweep found as M05.
    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    expect(harness.projectsApi.updateSettings).toHaveBeenCalledWith(7, true);
  });

  it('renders the toggle from the server response rather than from the click', () => {
    // The server is the authority: the box shows what came back, not what was clicked.
    harness.projectsApi.updateSettings.mockReturnValue(
      of({ ...project, autoDoneOnMerge: true }),
    );

    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    expect(harness.fixture.componentInstance.project()!.autoDoneOnMerge).toBe(true);
    expect(checkbox(harness.fixture).checked).toBe(true);
  });

  it('puts the box back and explains itself when the save fails', () => {
    harness.projectsApi.updateSettings.mockReturnValue(
      throwError(() => ({ status: 500 })),
    );

    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    // Without the explicit revert the browser leaves the box ticked while nothing was saved,
    // and the bound signal is unchanged so Angular never corrects it.
    expect(checkbox(harness.fixture).checked).toBe(false);
    expect(harness.fixture.componentInstance.error()).toContain('Failed to update project settings');
  });

  it('confirms what was turned on, not the opposite', () => {
    // The two messages are each other's negation, so swapping the branches is invisible
    // unless a test pins the message to the state it describes. The mutation sweep found
    // exactly that swap surviving.
    checkbox(harness.fixture).click();
    harness.fixture.detectChanges();

    expect(harness.toast.show).toHaveBeenCalledWith(
      'Merged pull requests will move their task to Done',
      'info',
    );
  });

  it('confirms what was turned off', () => {
    // beforeEach already instantiated the TestBed for the default project; this test needs
    // one that starts enabled, so the module is reset before it is reconfigured.
    TestBed.resetTestingModule();

    const enabled: ProjectDto = { ...project, autoDoneOnMerge: true };
    const off = setup(enabled);
    off.projectsApi.updateSettings.mockReturnValue(of({ ...enabled, autoDoneOnMerge: false }));

    expect(checkbox(off.fixture).checked).toBe(true);

    checkbox(off.fixture).click();
    off.fixture.detectChanges();

    expect(off.toast.show).toHaveBeenCalledWith(
      'Merged pull requests will no longer move their task',
      'info',
    );
  });
});
