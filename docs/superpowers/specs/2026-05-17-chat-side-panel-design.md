# Chat Side Panel Design

**Date:** 2026-05-17
**Status:** Approved

---

## Summary

Replace the full-page `/chat/:projectId` route with a persistent side panel that slides in from the right, pushing main content left. The panel shows the current project's chat and supports a fullscreen mode.

---

## Section 1: State & Service

**New service:** `ChatPanelService` (`client/src/app/core/services/chat-panel.service.ts`)

```ts
isOpen   = signal(false)
isFullscreen = signal(false)

toggle()
close()
toggleFullscreen()
```

- `providedIn: 'root'` — single instance across the app
- The sub-header "Chat" nav link calls `toggle()` instead of `routerLink="/chat/..."`
- The `/chat/:projectId` route is removed entirely

---

## Section 2: Layout

**File to modify:** the main authenticated layout component that wraps `<router-outlet>`.

Structure changes to a flex row:

```html
<div class="flex flex-1 overflow-hidden">
  <main [style.width]="panelOpen && fullscreen ? '0' : '100%'" class="transition-all duration-300 overflow-auto">
    <router-outlet />
  </main>
  <app-chat-panel />
</div>
```

Panel width behaviour (via CSS transition on `width`):

| State | Panel width | Main content |
|---|---|---|
| Closed | `0` (hidden) | `100%` |
| Open normal | `380px` | shrinks left |
| Fullscreen | `100%` | hidden (`width: 0`) |

Transitions: `transition: width 300ms ease` on both panel and main content for a smooth slide.

---

## Section 3: Chat Panel Component

**New component:** `ChatPanelComponent` (`client/src/app/chat/chat-panel.component.ts`)

Always mounted in the layout shell — not routed.

### Header
- Project name (derived from active route)
- Fullscreen toggle button (expand ↔ compress icon)
- Close (×) button — calls `chatPanelService.close()`

### Body
- Messages list (from current `ChatPageComponent` template)
- Input bar with paste, file picker, and send (from current `ChatPageComponent`)

### Project tracking
- Injects `Router` and listens to `NavigationEnd` events
- Traverses `router.routerState.root` to find `projectId` param in the active route tree
- On `projectId` change → `chatService.connect(projectId)`
- When navigating outside a project context (no `projectId` in route) → `chatService.disconnect()`, panel close button still works but chat body shows "Select a project to chat"

### Cleanup
- `ChatPageComponent` is deleted — its template and logic move into `ChatPanelComponent`
- The route `{ path: 'chat/:projectId', component: ChatPageComponent }` is removed from `app.routes.ts`
- `ChatService` is unchanged

---

## What Does NOT Change

- `ChatService` — signals, hub connection, history loading, image upload
- `ChatHub` (backend) — no changes
- `ChatController` (backend) — no changes
- Access control — panel still connects via the same `chatService.connect()` which enforces membership
