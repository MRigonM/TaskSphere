import { Injectable } from '@angular/core';
import { fromEvent, Observable, filter, map } from 'rxjs';

/**
 * "The user went to GitHub and came back."
 *
 * GitHub's installation settings page cannot redirect back to us, so the return is detected in
 * the browser instead. The flag is what makes this safe: an unarmed refocus is an ordinary tab
 * switch and must not spend a GitHub call, so only a refocus that follows a click on our own
 * link counts.
 */
@Injectable({ providedIn: 'root' })
export class GitHubReturnSyncService {
  private armed = false;

  readonly returned$: Observable<void> = fromEvent(document, 'visibilitychange').pipe(
    filter(() => document.visibilityState === 'visible' && this.armed),
    // Disarmed as the event passes, not by the subscriber: two subscribers must not each get a
    // turn from one trip, and a subscriber that throws must not leave it armed forever.
    map(() => {
      this.armed = false;
    }),
  );

  arm() {
    this.armed = true;
  }
}
