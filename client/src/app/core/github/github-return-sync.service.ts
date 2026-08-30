import { Injectable } from '@angular/core';
import { fromEvent, Observable, filter, map, share } from 'rxjs';

/**
 * "The user went to GitHub and came back."
 *
 * GitHub's installation settings page cannot redirect back to us, so the return is detected in
 * the browser instead. The arm is what makes this safe: an unarmed refocus is an ordinary tab
 * switch and must not spend a GitHub call, so only a refocus that follows a click on our own
 * link counts.
 *
 * **This is the only never-completing observable in this client.** Every other subscribe in the
 * app is on a finite HTTP observable that tears itself down. Consumers of `returned$` MUST
 * unsubscribe — `takeUntilDestroyed(this.destroyRef)` — or a destroyed component's handler keeps
 * firing and, worse, keeps winning: see the note on `share` below.
 */
@Injectable({ providedIn: 'root' })
export class GitHubReturnSyncService {
  /**
   * How long an arm stays good. Matches `GitHubInstallStateService.Lifetime` on the server,
   * which bounds the same trip: long enough to pick repositories on GitHub, short enough that a
   * forgotten arm stops being able to fire.
   */
  private static readonly ArmLifetimeMs = 10 * 60 * 1000;

  private armedAt: number | null = null;

  /**
   * `share` is load-bearing, not an optimisation. Without it `fromEvent` is cold and registers
   * one listener per subscriber; DOM dispatch runs them in registration order, so the first
   * listener's `map` would clear the arm and every later subscriber would be starved by a
   * `filter` that now reads false. One trip would notify exactly one component — and after a
   * re-navigation, the one it notified would be a destroyed one.
   *
   * With `share` there is a single upstream listener, so the arm is cleared once and all live
   * subscribers receive that single emission.
   */
  readonly returned$: Observable<void> = fromEvent(document, 'visibilitychange').pipe(
    filter(() => document.visibilityState === 'visible' && this.isArmed()),
    // Cleared as the value passes rather than by the subscriber: a handler that throws must not
    // strand the arm in a state where the next ordinary alt-tab spends a GitHub call.
    map(() => {
      this.armedAt = null;
    }),
    share(),
  );

  arm() {
    this.armedAt = Date.now();
  }

  /**
   * An arm that never got its return trip must expire. The pipeline only runs while something is
   * subscribed, so arming and then navigating away leaves the flag set with nothing to clear it;
   * without a lifetime it would survive until reload and fire on an unrelated alt-tab hours
   * later — the exact behaviour the arm exists to prevent.
   */
  private isArmed(): boolean {
    if (this.armedAt === null) return false;

    if (Date.now() - this.armedAt >= GitHubReturnSyncService.ArmLifetimeMs) {
      this.armedAt = null;
      return false;
    }

    return true;
  }
}
