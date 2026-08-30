# Project Activity Refresh — Design Spec

**Date:** 2026-08-25
**Project:** TaskSphere
**Sub-project:** D (smart commits) — second slice
**Status:** Approved

---

## Goal

A task whose pull request was merged reaches `Done` because someone opened the board, not because someone pressed **Sync all repositories**.

---

## Why this exists

Merge → Done shipped and works, but its only trigger is the manual company-wide sync. Two problems follow from that, both observed live on 2026-08-25:

1. **The user has to know to press a button** on an unrelated screen — the Sync control lives in a task's Activity tab, not on the board where the task's status is read.
2. **Sync is `Company`-gated**, so a member cannot trigger the transition at all.

The board is where Done-ness is read, so the board is where the refresh belongs.

---

## Why the cheap path exists

`GitHubActivitySyncService.SyncPullRequestsAsync` is **one GitHub call per repository** (`/repos/{full_name}/pulls?state=all&sort=updated&direction=desc&per_page=100`). The expensive part of a full sync is the commits pass, which costs `1 + B` calls per repository for B branches.

The merge → Done transition reads **head branches of pull requests** and nothing else. It never reads commits or branches — that independence is why it can run before or after the resolver. So the transition's entire input is available for one call per repository, and the branch walk is being paid for a fact it does not need.

This is the whole reason a project-scoped automatic refresh is affordable where an automatic full sync would not be.

---

## Scope Decisions

### The trigger is opening a board or a backlog

A sprint board or backlog page, once it resolves its project, refreshes pull requests for that project's linked repositories and runs the transition.

**Not** on every sprint switch — switching sprints cannot change what GitHub knows, and the cooldown would suppress it anyway.

**Not** a background timer. A timer-driven transition has no user behind it, which reopens the audit-actor decision taken on 2026-08-24: `system` was rejected as unaccountable. Every transition this feature causes still has a real person who caused it.

**Not** webhooks. Real-time delivery (sub-project B2) remains the correct endgame and is still blocked on the deployment being local-only. This feature is not a substitute for it and does not make it unnecessary.

### Members can trigger it, scoped to their projects

The existing sync is `Company`-only because it spends company-wide rate limit across every linked repository. This refresh is bounded by one project's repositories and costs roughly one call each.

The authorizing fact is the one create-branch-from-task already relies on: **the repository↔project link**. A member can refresh only repositories linked to a project they belong to.

The alternative — admin-only — was rejected because it makes the board disagree with itself: whether a task shows as `Done` would depend on whether an admin happened to open it last.

### A project with `AutoDoneOnMerge` off is skipped entirely

Checked **before** the installation lookup, so a company with no GitHub connection at all pays nothing and reports nothing.

**Accepted cost:** for opted-out projects the pull-request mirror stays exactly as stale as it is today, until someone presses Sync. This feature buys freshness only where freshness does work.

### The board never waits for GitHub

The page loads at its current speed and fires the refresh alongside. If the response reports a transition, the page re-reads and the card moves — through the machinery added on 2026-08-25 for the manual sync, which also re-points an open modal.

**Accepted cost:** the old status is visible for a moment. In exchange, GitHub being slow, unreachable, or not connected never delays or breaks a board.

### No toast when a task moves

Rejected deliberately (Rigon, 2026-08-25). A card will change column silently while someone is looking at the board. The board is already a surface where things move.

---

## Data Layer

### `GitHubRepository.PullRequestsRefreshedAtUtc`

```csharp
public DateTime? PullRequestsRefreshedAtUtc { get; set; }
```

Nullable; null means never refreshed by this path. New EF Core migration.

**The cooldown is per repository, not per project.** One repository can be linked to several projects, and refreshing it once serves every board that shows it. Five people opening boards in the same minute cost one call, not five.

**Window: 60 seconds.** Chosen against merge → alt-tab → look at the board, which is often under a minute; a longer window would leave people reaching for the Sync button, which is the behaviour this feature exists to remove.

**This column carries the same hazard as `MergeTransitionAppliedAtUtc`.** It is TaskSphere's own, not GitHub-sourced, and the repository upsert in `GitHubRepositorySyncService` must not overwrite it. Clearing it there would silently reset every cooldown on every repository sync. It needs the same source-level guard `GitHubActivitySyncService` carries for the merge marker — and the risk is concrete: that upsert assigns `GitHubInstallationId`, `FullName`, `DefaultBranch`, `IsPrivate`, `IsDeleted` and `DeletedAt` field-by-field (`GitHubRepositorySyncService.cs:133-138`), which is precisely the block a future edit would add a new column to.

---

## Components

### The pull-request listing is promoted out of the sync service

`SyncPullRequestsAsync` is currently private to `GitHubActivitySyncService`. Two callers need it, so it becomes a unit both call — the same extraction `TaskKeyResolutionMap` received, and with the same gate:

> The extraction is behaviour-preserving. `GitHubActivitySyncTests` must pass **unchanged** before any new behaviour is added.

### `IProjectActivityRefreshService` (new)

Lives in `TaskSphere.Application/Interfaces`, implemented in `TaskSphere.Infrastructure/Services`.

A separate service rather than a second public method on `GitHubActivitySyncService`: that file is ~470 lines and already mixes concerns, and this operation has a different authorization boundary (member-reachable, project-scoped) from everything else in it. The same argument produced `MergeTransitionService`.

```csharp
Task<Result<ProjectActivityRefreshDto>> RefreshAsync(
    Guid companyId,
    int projectId,
    string userId,
    bool isCompanyAdmin,
    string? actorUsername,
    CancellationToken cancellationToken = default);
```

Algorithm:

1. Load the project, company-scoped. Missing → failure.
2. Authorize through `IAccessControlService.CanAccessProjectAsync(companyId, userId, projectId, ct)` for `User` callers; company admins bypass.
3. `AutoDoneOnMerge == false` → return `Refreshed: false`, no GitHub call, no installation lookup.
4. Installation lookup. None → failure (`GitHub.NotConnected`).
5. Repositories linked to **this project**, filtered to those whose `PullRequestsRefreshedAtUtc` is null or older than the cooldown.
6. Per repository: fetch the listing, upsert, stamp the column, `SaveChangesAsync`. **Per repository, not once at the end** — the reported unit must equal the persisted unit, and a failed unit must be discarded rather than left tracked in the shared `DbContext`.
7. Run the transition with the repository filter below.
8. Return the counts.

A repository that fails is counted, not thrown: it does not stop the pass and does not stamp its cooldown.

### The transition gains a repository filter — not a project filter

`IMergeTransitionService.ApplyAsync` gains an optional set of repository ids. Null means the whole company, which is what the admin sync passes.

**Why not filter by project, which is what "project-scoped" first suggests.** A repository can be linked to several projects, so one pull request's head branch can name keys belonging to projects outside the filter. Both ways of handling that break something:

- **Skip the out-of-scope keys and stamp the marker** → those tasks are stranded forever; the pull request is never reconsidered.
- **Skip them and leave the marker null** → the pull request stays eligible, and a later pass re-applies the transition to a task a human had deliberately moved back. That is precisely what the marker exists to prevent.

Filtering on repositories avoids both. Every pull request considered is considered **fully** — all of its keys, then stamped once — so the marker keeps meaning "considered", and the once-only and human-override guarantees are untouched. What the filter buys is that opening one project's board cannot move tasks in repositories that project has nothing to do with.

### `POST /api/Projects/{projectId}/github-refresh` (new)

On `ProjectsController`, not `GitHubController`.

This follows the split sub-project C established: member-reachable GitHub work lives on the resource's own controller (`Tasks/{id}/github-activity`, `CompanyOrUser`), while company-wide spend stays on `GitHubController` behind `Company`. `TasksController` already carries a GitHub service for the same reason.

```csharp
[HttpPost("{projectId:int}/github-refresh")]
public async Task<IActionResult> RefreshGitHub(int projectId, CancellationToken ct)
```

- Class-level `[Authorize(Roles = Roles.CompanyOrUser)]`; **no action-level attribute**, so membership is enforced in the service and the response carries `Auth.Forbidden` rather than a bare framework 403.
- **Not `[Audit]`, deliberately.** Every audited action is a human decision; this fires from opening a page, and auditing it would bury the merge → Done entries under one row per board visit. The transitions it causes remain audited individually.

```csharp
public record ProjectActivityRefreshDto(
    bool Refreshed,            // false when the cooldown or the toggle suppressed it
    int RepositoriesRefreshed,
    int TasksTransitioned);
```

`TasksTransitioned` is the only field the client acts on. The other two exist so the behaviour is observable in tests and when answering "why didn't it move".

### Client

`ProjectsApiService.refreshGitHub(projectId)`.

Both pages — `sprints-page` (board) and `tasks-page` (backlog) — fire it once per project load, alongside their normal load rather than before it. A response with `TasksTransitioned > 0` triggers the existing re-read path added on 2026-08-25, which also re-points an open modal.

**Failures are swallowed.** No error signal, no banner, no toast. The user did not ask for this call, so an error about it is noise about something they did not do; a board showing a red banner because a background refresh failed reads as a broken board; and the manual Sync button remains the visible, diagnosable path. `Refreshed: false` is indistinguishable from a quiet refresh and needs no UI.

---

## Failure Handling

| Condition | Result |
|---|---|
| Project not found / other company | `Result` failure; client swallows |
| Caller is a `User` and not a member | `Auth.Forbidden`; client swallows |
| `AutoDoneOnMerge` off | Success, `Refreshed: false`, zero calls |
| No GitHub installation | `GitHub.NotConnected` failure; client swallows |
| Cooldown suppressed every repository | Success, `Refreshed: false`, zero calls |
| One repository's listing fails | Counted; other repositories still refreshed; its cooldown not stamped |
| GitHub unreachable | Board renders normally; nothing on screen changes |

---

## Testing

TDD throughout. Beyond the happy path, these carry the design:

1. **The extraction is behaviour-preserving.** `GitHubActivitySyncTests` pass unchanged before any new behaviour exists.
2. **The repository filter, both directions.** A pull request in a repository outside the filter is neither transitioned nor stamped. A pull request inside the filter whose head branch names two projects' keys moves both and stamps once.
3. **The cooldown.** Two refreshes inside the window make one GitHub call; past the window, two. Asserted against the fake API client's call count.
4. **The cooldown survives a repository sync.** Source-level guard, mutation-checked — a repository upsert that cleared `PullRequestsRefreshedAtUtc` would reset every cooldown silently.
5. **The opt-out costs nothing.** A project with the toggle off produces no GitHub call and no installation lookup.
6. **The dangerous direction.** A `User` who is not a member of the project gets `Auth.Forbidden`. This is the first member-triggered GitHub spend in the app.
7. **Partial failure.** One repository failing leaves the others refreshed and their work persisted; the failed unit is discarded rather than left tracked.
8. **The endpoint contract**, by reflection: the route string the client hardcodes, the class-level gate with no action-level override, and the **absence** of `[Audit]` — the absence is a decision, so a test should state it.

Client: the pages refresh once per project load; a non-zero count triggers exactly one re-read; a zero count triggers none; a failed refresh renders the board and sets no error. Bindings driven through the template and mutation-checked — a handler called directly cannot witness a binding.

**Fixture requirement — mandatory.** Projects, repositories, tasks and pull requests seeded with **different identity values**, using decoy rows. A freshly migrated database gives every table the same identity seeds, so a lookup passing the wrong entity's id resolves correctly by accident — the trap that survived nineteen tests on create-branch-from-task.

**Mutation sweep** over the finished feature, measured independently of whoever implements it. A survivor is evidence about the tests, not about the app.

---

## Out of Scope

- Webhooks (B2) and any real-time trigger
- Refreshing commits or branches on this path — pull requests only
- The on-demand **repository** sync the GitHub connection screen implies exists (a separate open item, unrelated boundary)
- Any transition other than merge → `Done`
- A toast or any other announcement when a task moves
- Making the manual Sync button go away — it still owns commits, branches, and the whole-company case

---

## Live Verification

Cannot be confirmed by tests:

1. With `AutoDoneOnMerge` **on**: merge a pull request on GitHub, switch to TaskSphere, open the board. The task reaches `Done` **without pressing Sync**.
2. Open the board again immediately — the cooldown suppresses the call, and nothing changes.
3. Wait past the window and reopen — a fresh call is made, and nothing moves, because the marker already applied.
4. As a **member rather than an admin**, repeat step 1.
5. With the toggle **off**, confirm no task moves and no GitHub call is made.
6. Move a transitioned task back to `InProgress` by hand, reopen the board, and confirm it **stays** — the human override survives the new trigger.
7. Disconnect GitHub (or block it) and confirm the board still loads with nothing on screen about it.

Record which of these were actually exercised and which were not.
