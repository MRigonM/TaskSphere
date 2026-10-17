import { describe, it, expect, afterEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';

import { GitHubReturnSyncService } from './github-return-sync.service';

function becomeVisible() {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
  document.dispatchEvent(new Event('visibilitychange'));
}

function setup() {
  TestBed.configureTestingModule({ providers: [GitHubReturnSyncService] });
  const service = TestBed.inject(GitHubReturnSyncService);
  const seen = vi.fn();
  const sub = service.returned$.subscribe(seen);
  return { service, seen, sub };
}

describe('GitHubReturnSyncService', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('does not fire on an ordinary tab switch', () => {
    const { seen, sub } = setup();

    becomeVisible();

    // Unarmed refocus is just the user coming back to the tab. Firing here would spend a
    // GitHub call on every alt-tab.
    expect(seen).toHaveBeenCalledTimes(0);
    sub.unsubscribe();
  });

  it('fires once after arming', () => {
    const { service, seen, sub } = setup();

    service.arm();
    becomeVisible();

    expect(seen).toHaveBeenCalledTimes(1);
    sub.unsubscribe();
  });

  it('disarms itself, so a second refocus does not fire again', () => {
    const { service, seen, sub } = setup();

    service.arm();
    becomeVisible();
    becomeVisible();

    // The count must not grow. Asserting only "was called" would pass with the flag never
    // cleared, which is the actual defect this guards.
    expect(seen).toHaveBeenCalledTimes(1);
    sub.unsubscribe();
  });

  it('can be re-armed for a second trip to GitHub', () => {
    const { service, seen, sub } = setup();

    service.arm();
    becomeVisible();
    service.arm();
    becomeVisible();

    expect(seen).toHaveBeenCalledTimes(2);
    sub.unsubscribe();
  });

  it('notifies every live subscriber from one trip, not just the first', () => {
    TestBed.configureTestingModule({ providers: [GitHubReturnSyncService] });
    const service = TestBed.inject(GitHubReturnSyncService);

    const first = vi.fn();
    const second = vi.fn();
    const subA = service.returned$.subscribe(first);
    const subB = service.returned$.subscribe(second);

    service.arm();
    becomeVisible();

    // Without share(), fromEvent registers a listener per subscriber and the first one to run
    // clears the arm, so the second's filter reads false and it never fires. Two screens both
    // showing repositories both need to know.
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);

    // Still one-shot across all of them.
    becomeVisible();
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);

    subA.unsubscribe();
    subB.unsubscribe();
  });

  it('lets an arm expire, so a forgotten trip cannot fire hours later', () => {
    const { service, seen, sub } = setup();

    service.arm();

    // The user armed, then navigated away instead of returning. Nothing was subscribed when the
    // event would have fired, so nothing ever cleared the flag.
    vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 11 * 60 * 1000);

    becomeVisible();

    // An ordinary alt-tab eleven minutes later must not spend a GitHub call.
    expect(seen).toHaveBeenCalledTimes(0);
    sub.unsubscribe();
  });
});
