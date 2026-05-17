# Chat Side Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the full-page `/chat/:projectId` route with a persistent side panel that slides in from the right, pushes main content left, and supports fullscreen mode.

**Architecture:** A new `ChatPanelService` holds `isOpen`, `isFullscreen`, and `projectId` signals. The app shell (`app.html`) becomes a flex row — main content + panel side by side. `ChatPanelComponent` mounts in the shell (always present, not routed), reads projectId from the service, and connects `ChatService` via `effect()`. Sub-header "Chat" link becomes a button that calls `toggle()`.

**Tech Stack:** Angular 21 (signals, effect, standalone components), Tailwind CSS, `@microsoft/signalr` (unchanged), Vitest

---

## File Map

| Action | File |
|---|---|
| **Create** | `client/src/app/core/services/chat-panel.service.ts` |
| **Create** | `client/src/app/chat/chat-panel.component.ts` |
| **Create** | `client/src/app/chat/chat-panel.component.html` |
| **Modify** | `client/src/app/app.html` |
| **Modify** | `client/src/app/app.ts` |
| **Modify** | `client/src/app/layout/sub-header/sub-header.component.ts` |
| **Modify** | `client/src/app/layout/sub-header/sub-header.component.html` |
| **Modify** | `client/src/app/app.routes.ts` |
| **Delete** | `client/src/app/chat/chat-page.component.ts` |
| **Delete** | `client/src/app/chat/chat-page.component.html` |

---

## Task 1: ChatPanelService

**Files:**
- Create: `client/src/app/core/services/chat-panel.service.ts`
- Test: `client/src/app/core/services/chat-panel.service.spec.ts`

- [ ] **Step 1: Write the failing tests**

Create `client/src/app/core/services/chat-panel.service.spec.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { ChatPanelService } from './chat-panel.service';

describe('ChatPanelService', () => {
  it('starts closed and not fullscreen', () => {
    const svc = new ChatPanelService();
    expect(svc.isOpen()).toBe(false);
    expect(svc.isFullscreen()).toBe(false);
    expect(svc.projectId()).toBeNull();
  });

  it('toggle() opens when closed', () => {
    const svc = new ChatPanelService();
    svc.toggle();
    expect(svc.isOpen()).toBe(true);
  });

  it('toggle() closes when open', () => {
    const svc = new ChatPanelService();
    svc.toggle();
    svc.toggle();
    expect(svc.isOpen()).toBe(false);
  });

  it('close() sets isOpen false and clears fullscreen', () => {
    const svc = new ChatPanelService();
    svc.toggle();
    svc.toggleFullscreen();
    svc.close();
    expect(svc.isOpen()).toBe(false);
    expect(svc.isFullscreen()).toBe(false);
  });

  it('toggleFullscreen() flips isFullscreen', () => {
    const svc = new ChatPanelService();
    svc.toggleFullscreen();
    expect(svc.isFullscreen()).toBe(true);
    svc.toggleFullscreen();
    expect(svc.isFullscreen()).toBe(false);
  });

  it('setProjectId() updates projectId signal', () => {
    const svc = new ChatPanelService();
    svc.setProjectId(42);
    expect(svc.projectId()).toBe(42);
    svc.setProjectId(null);
    expect(svc.projectId()).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd client && npm test -- --reporter=verbose chat-panel.service.spec
```

Expected: `Cannot find module './chat-panel.service'`

- [ ] **Step 3: Create the service**

Create `client/src/app/core/services/chat-panel.service.ts`:

```ts
import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ChatPanelService {
  isOpen = signal(false);
  isFullscreen = signal(false);
  projectId = signal<number | null>(null);

  toggle(): void { this.isOpen.update(v => !v); }
  close(): void { this.isOpen.set(false); this.isFullscreen.set(false); }
  toggleFullscreen(): void { this.isFullscreen.update(v => !v); }
  setProjectId(id: number | null): void { this.projectId.set(id); }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd client && npm test -- --reporter=verbose chat-panel.service.spec
```

Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add client/src/app/core/services/chat-panel.service.ts client/src/app/core/services/chat-panel.service.spec.ts
git commit -m "feat: add ChatPanelService with open/fullscreen/projectId signals"
```

---

## Task 2: ChatPanelComponent

**Files:**
- Create: `client/src/app/chat/chat-panel.component.ts`
- Create: `client/src/app/chat/chat-panel.component.html`

The component replaces `ChatPageComponent`. It mounts in the app shell (always present), reads `projectId` from `ChatPanelService`, and connects `ChatService` via `effect()`.

- [ ] **Step 1: Create the component TypeScript**

Create `client/src/app/chat/chat-panel.component.ts`:

```ts
import { Component, effect, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../core/services/chat.service';
import { ChatPanelService } from '../core/services/chat-panel.service';
import { AuthStoreService } from '../core/services/auth-store.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-chat-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './chat-panel.component.html',
})
export class ChatPanelComponent {
  @ViewChild('messagesContainer') messagesContainer!: ElementRef<HTMLDivElement>;

  messageInput = '';
  uploading = false;

  constructor(
    protected chatService: ChatService,
    protected panelService: ChatPanelService,
    protected authStore: AuthStoreService,
  ) {
    effect(() => {
      const id = this.panelService.projectId();
      if (id) {
        this.chatService.connect(id);
      } else {
        this.chatService.disconnect();
      }
    });
  }

  send(): void {
    const content = this.messageInput.trim();
    if (!content) return;
    this.chatService.sendMessage(content);
    this.messageInput = '';
  }

  onPaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items;
    if (!items) return;
    for (let i = 0; i < items.length; i++) {
      if (items[i].type.startsWith('image/')) {
        event.preventDefault();
        const file = items[i].getAsFile();
        if (file) this.uploadAndSend(file);
        return;
      }
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.uploadAndSend(file);
    input.value = '';
  }

  private uploadAndSend(file: File): void {
    this.uploading = true;
    this.chatService.uploadImage(file).subscribe({
      next: (res) => {
        this.chatService.sendMessage(this.messageInput.trim(), res.url);
        this.messageInput = '';
        this.uploading = false;
      },
      error: () => { this.uploading = false; },
    });
  }

  resolveImageUrl(url: string): string {
    if (url.startsWith('http')) return url;
    return environment.apiUrl.replace('/api/', '') + url;
  }

  openImage(url: string): void {
    window.open(this.resolveImageUrl(url), '_blank');
  }

  isOwnMessage(senderId: string): boolean {
    return senderId === this.authStore.auth()?.userId;
  }
}
```

- [ ] **Step 2: Create the component template**

Create `client/src/app/chat/chat-panel.component.html`:

```html
@if (panelService.isOpen()) {
  <div
    class="flex flex-col border-l border-white/10 bg-slate-950 overflow-hidden transition-all duration-300 flex-shrink-0"
    [style.width]="panelService.isFullscreen() ? '100%' : '380px'"
  >
    <!-- Panel header -->
    <div class="flex items-center justify-between px-4 py-3 border-b border-white/10 flex-shrink-0">
      <div class="flex items-center gap-2">
        <span class="text-sm font-semibold text-white">Team Chat</span>
        <span
          class="text-xs px-2 py-0.5 rounded-full"
          [class.bg-emerald-500/15]="chatService.connected()"
          [class.text-emerald-400]="chatService.connected()"
          [class.bg-red-500/15]="!chatService.connected()"
          [class.text-red-400]="!chatService.connected()"
        >
          {{ chatService.connected() ? 'Live' : 'Off' }}
        </span>
      </div>
      <div class="flex items-center gap-1">
        <!-- Fullscreen toggle -->
        <button
          (click)="panelService.toggleFullscreen()"
          class="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-white/10 transition"
          [title]="panelService.isFullscreen() ? 'Collapse' : 'Expand'"
        >
          @if (panelService.isFullscreen()) {
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 9L4 4m0 0l5 0M4 4l0 5M15 9l5-5m0 0l-5 0m5 0l0 5M9 15l-5 5m0 0l5 0m-5 0l0-5M15 15l5 5m0 0l-5 0m5 0l0-5" />
            </svg>
          } @else {
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-5h-4m4 0v4m0-4l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
            </svg>
          }
        </button>
        <!-- Close -->
        <button
          (click)="panelService.close()"
          class="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-white/10 transition"
          title="Close"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>
    </div>

    @if (!panelService.projectId()) {
      <!-- No project selected -->
      <div class="flex-1 flex items-center justify-center text-slate-500 text-sm">
        Select a project to start chatting
      </div>
    } @else {
      <!-- Messages -->
      <div
        #messagesContainer
        class="flex-1 overflow-y-auto p-4 space-y-4"
      >
        @if (chatService.messages().length === 0) {
          <div class="flex items-center justify-center h-full text-slate-500 text-sm">
            No messages yet. Start the conversation!
          </div>
        }

        @for (msg of chatService.messages(); track msg.id) {
          <div
            class="flex flex-col"
            [class.items-end]="isOwnMessage(msg.senderId)"
            [class.items-start]="!isOwnMessage(msg.senderId)"
          >
            <span class="text-xs text-slate-500 mb-1">
              {{ isOwnMessage(msg.senderId) ? 'You' : msg.senderName }}
            </span>
            <div
              class="max-w-[85%] px-3 py-2 rounded-xl text-sm"
              [class.bg-indigo-500]="isOwnMessage(msg.senderId)"
              [class.text-white]="isOwnMessage(msg.senderId)"
              [class.bg-white/10]="!isOwnMessage(msg.senderId)"
              [class.text-slate-200]="!isOwnMessage(msg.senderId)"
            >
              @if (msg.imageUrl) {
                <img
                  [src]="resolveImageUrl(msg.imageUrl)"
                  alt="Shared image"
                  class="rounded-lg max-w-full max-h-48 cursor-pointer"
                  (click)="openImage(msg.imageUrl)"
                />
              }
              @if (msg.content) {
                <p [class.mt-2]="msg.imageUrl">{{ msg.content }}</p>
              }
            </div>
            <span class="text-xs text-slate-600 mt-1">
              {{ msg.sentAt | date: 'shortTime' }}
            </span>
          </div>
        }
      </div>

      <!-- Input -->
      <div class="p-3 border-t border-white/10 flex gap-2 flex-shrink-0">
        <input
          type="text"
          [(ngModel)]="messageInput"
          (keydown.enter)="send()"
          (paste)="onPaste($event)"
          placeholder="Message..."
          class="flex-1 min-w-0 rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-sm text-white placeholder-slate-500 outline-none focus:border-indigo-500/50 focus:ring-1 focus:ring-indigo-500/50 transition"
          [disabled]="!chatService.connected() || uploading"
        />
        <input
          #fileInput
          type="file"
          accept="image/jpeg,image/png,image/gif,image/webp"
          class="hidden"
          (change)="onFileSelected($event)"
        />
        <button
          (click)="fileInput.click()"
          [disabled]="!chatService.connected() || uploading"
          class="rounded-xl border border-white/10 bg-white/5 px-2.5 py-2 text-slate-400 hover:text-white hover:bg-white/10 disabled:opacity-40 disabled:cursor-not-allowed transition"
          title="Attach image"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
          </svg>
        </button>
        <button
          (click)="send()"
          [disabled]="!chatService.connected() || uploading || !messageInput.trim()"
          class="rounded-xl bg-indigo-500 px-3 py-2 text-sm font-semibold text-white hover:bg-indigo-400 disabled:opacity-40 disabled:cursor-not-allowed transition"
        >
          {{ uploading ? '...' : 'Send' }}
        </button>
      </div>
    }
  </div>
}
```

- [ ] **Step 3: Verify the app builds**

```bash
cd client && npm run build 2>&1 | tail -20
```

Expected: build succeeds (ChatPanelComponent isn't wired into the shell yet, so no runtime effect).

- [ ] **Step 4: Commit**

```bash
git add client/src/app/chat/chat-panel.component.ts client/src/app/chat/chat-panel.component.html
git commit -m "feat: add ChatPanelComponent as persistent side panel"
```

---

## Task 3: Update App Shell Layout

**Files:**
- Modify: `client/src/app/app.ts`
- Modify: `client/src/app/app.html`

Wire `ChatPanelComponent` into the app shell and change the layout to a flex row.

- [ ] **Step 1: Update `app.ts`**

Replace the full content of `client/src/app/app.ts`:

```ts
import { Component, computed } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NgIf } from '@angular/common';
import { FooterComponent } from './layout/footer/footer.component';
import { HeaderComponent } from './layout/header/header.component';
import { SubHeaderComponent } from './layout/sub-header/sub-header.component';
import { ToastComponent } from './layout/toast/toast.component';
import { ChatPanelComponent } from './chat/chat-panel.component';
import { AuthStoreService } from './core/services/auth-store.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, FooterComponent, HeaderComponent, SubHeaderComponent, NgIf, ToastComponent, ChatPanelComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  constructor(private auth: AuthStoreService) {}
  year = new Date().getFullYear();
  isLoggedIn = computed(() => this.auth.isLoggedIn());
}
```

- [ ] **Step 2: Update `app.html`**

Replace the full content of `client/src/app/app.html`:

```html
<div class="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
  <app-header></app-header>
  <app-sub-header *ngIf="isLoggedIn()"></app-sub-header>
  <div class="flex flex-1 overflow-hidden">
    <main class="flex-1 min-w-0 overflow-auto">
      <div class="mx-auto w-full max-w-6xl px-4 py-8">
        <router-outlet></router-outlet>
      </div>
    </main>
    <app-chat-panel></app-chat-panel>
  </div>
  <app-footer [year]="year"></app-footer>
  <app-toast></app-toast>
</div>
```

- [ ] **Step 3: Verify build**

```bash
cd client && npm run build 2>&1 | tail -20
```

Expected: build succeeds. Panel not yet openable (no toggle wired) but layout is correct.

- [ ] **Step 4: Commit**

```bash
git add client/src/app/app.ts client/src/app/app.html
git commit -m "feat: add chat side panel to app shell layout"
```

---

## Task 4: Update Sub-Header

**Files:**
- Modify: `client/src/app/layout/sub-header/sub-header.component.ts`
- Modify: `client/src/app/layout/sub-header/sub-header.component.html`

Change the "Chat" nav link to a button that calls `chatPanelService.toggle()`. Also move `projectId` tracking into `ChatPanelService` so the panel knows which project to connect to. Remove `/chat/` from the URL pattern match (the route no longer exists).

- [ ] **Step 1: Update `sub-header.component.ts`**

Replace the full content:

```ts
import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthStoreService } from '../../core/services/auth-store.service';
import { ChatPanelService } from '../../core/services/chat-panel.service';

@Component({
  selector: 'app-sub-header',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './sub-header.component.html',
})
export class SubHeaderComponent {
  projectId = signal<number | null>(null);
  isCompany = computed(() => this.auth.isCompany());

  constructor(
    public auth: AuthStoreService,
    public chatPanel: ChatPanelService,
    private router: Router,
  ) {}

  ngOnInit() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(() => this.updateProjectId(this.router.url));

    this.updateProjectId(this.router.url);
  }

  private updateProjectId(url: string): void {
    const id = this.extractProjectId(url);
    this.projectId.set(id);
    this.chatPanel.setProjectId(id);
  }

  private extractProjectId(url: string): number | null {
    const m =
      url.match(/\/dashboard\/projects\/(\d+)/) ||
      url.match(/\/tasks\/(\d+)/) ||
      url.match(/\/sprints\/(\d+)/);

    const id = m ? Number(m[1]) : NaN;
    return Number.isFinite(id) && id > 0 ? id : null;
  }
}
```

- [ ] **Step 2: Update `sub-header.component.html`**

Replace the Chat `<a>` link (lines 36–43) with a button. Full file replacement:

```html
<nav class="border-b border-white/10 bg-slate-950/70 backdrop-blur">
  <div class="mx-auto max-w-6xl px-4 py-3">
    <div class="flex items-center gap-3 text-sm">
      <a
        routerLink="/dashboard/projects"
        class="text-slate-300 hover:text-white transition"
        routerLinkActive="text-white font-semibold"
        [routerLinkActiveOptions]="{ exact: true }"
      >
        Projects
      </a>

      <ng-container *ngIf="projectId() as pid">
        <span class="text-slate-600">/</span>

        <a
          [routerLink]="['/tasks', pid]"
          class="text-slate-300 hover:text-white transition"
          routerLinkActive="text-white font-semibold"
        >
          Backlog
        </a>

        <span class="text-slate-600">/</span>

        <a
          [routerLink]="['/sprints', pid]"
          class="text-slate-300 hover:text-white transition"
          routerLinkActive="text-white font-semibold"
        >
          Board
        </a>

        <span class="text-slate-600">/</span>

        <button
          (click)="chatPanel.toggle()"
          class="transition"
          [class.text-white]="chatPanel.isOpen()"
          [class.font-semibold]="chatPanel.isOpen()"
          [class.text-slate-300]="!chatPanel.isOpen()"
          [class.hover:text-white]="!chatPanel.isOpen()"
        >
          Chat
        </button>

        <ng-container *ngIf="isCompany()">
          <span class="text-slate-600">/</span>

          <a
            [routerLink]="['/dashboard/projects', 'sprints', pid]"
            class="text-slate-300 hover:text-white transition"
            routerLinkActive="text-white font-semibold"
          >
            Sprints
          </a>
        </ng-container>
      </ng-container>

      <span *ngIf="!projectId()" class="text-xs text-slate-500 ml-2">
        Select a project…
      </span>
    </div>
  </div>
</nav>
```

- [ ] **Step 3: Verify build and smoke test**

```bash
cd client && npm run build 2>&1 | tail -20
```

Then start the dev server and verify:
1. Navigate to a project — Chat button appears in sub-header
2. Click Chat — panel slides in from the right, main content shrinks
3. Click Chat again — panel closes
4. Click expand icon — panel goes fullscreen
5. Click collapse icon — panel returns to 380px
6. Click × — panel closes
7. Navigate away from a project — panel shows "Select a project to start chatting"

```bash
cd client && npm start
```

- [ ] **Step 4: Commit**

```bash
git add client/src/app/layout/sub-header/sub-header.component.ts client/src/app/layout/sub-header/sub-header.component.html
git commit -m "feat: wire Chat button to side panel toggle in sub-header"
```

---

## Task 5: Remove ChatPageComponent and Old Route

**Files:**
- Modify: `client/src/app/app.routes.ts`
- Delete: `client/src/app/chat/chat-page.component.ts`
- Delete: `client/src/app/chat/chat-page.component.html`

- [ ] **Step 1: Remove the chat route from `app.routes.ts`**

Replace the full content of `client/src/app/app.routes.ts`:

```ts
import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { LoginComponent } from './account/login/login.component';
import { RegisterComponent } from './account/register/register.component';
import { guestGuard } from './core/guards/guest.guard';
import { UsersDashboardComponent } from './company-dashboard/users/users-dashboard.component';
import { companyGuard } from './core/guards/company.guard';
import { ProjectComponent } from './company-dashboard/projects/projects.component';
import { SprintsPageComponent } from './sprints/sprints-page.component';
import { TasksPageComponent } from './tasks/tasks-page.component';
import { companyMemberGuard } from './core/guards/company-member.guard';
import { ProjectPageComponent } from './company-dashboard/projects/project-page.component';
import { ProjectSprintsComponent } from './company-dashboard/projects/project-sprints.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'account/login', component: LoginComponent, canActivate: [guestGuard] },
  { path: 'account/register', component: RegisterComponent, canActivate: [guestGuard] },
  { path: 'dashboard/users', component: UsersDashboardComponent, canActivate: [companyGuard] },
  { path: 'dashboard/projects', component: ProjectComponent, canActivate: [companyMemberGuard] },
  { path: 'dashboard/projects/:projectId', component: ProjectPageComponent, canActivate: [companyGuard] },
  { path: 'dashboard/projects/sprints/:projectId', component: ProjectSprintsComponent, canActivate: [companyGuard] },
  { path: 'sprints/:projectId', component: SprintsPageComponent, canActivate: [companyMemberGuard] },
  { path: 'tasks/:projectId', component: TasksPageComponent, canActivate: [companyMemberGuard] },
  { path: '**', redirectTo: '' },
];
```

- [ ] **Step 2: Delete ChatPageComponent files**

```bash
rm client/src/app/chat/chat-page.component.ts client/src/app/chat/chat-page.component.html
```

- [ ] **Step 3: Verify build is clean**

```bash
cd client && npm run build 2>&1 | tail -20
```

Expected: build succeeds with no references to `ChatPageComponent`.

- [ ] **Step 4: Final smoke test**

Start the dev server and verify the full happy path:
1. Login and navigate to a project
2. Click "Chat" in sub-header → panel slides in, loads message history
3. Send a text message → appears in real time
4. Paste an image → uploads and appears inline
5. Click expand → fullscreen
6. Click collapse → back to 380px side panel
7. Click × → panel closes
8. Navigate to `/chat/123` directly → redirected to home (wildcard route)

```bash
cd client && npm start
```

- [ ] **Step 5: Commit**

```bash
git add client/src/app/app.routes.ts
git rm client/src/app/chat/chat-page.component.ts client/src/app/chat/chat-page.component.html
git commit -m "feat: remove full-page chat route, side panel is now the only chat entry point"
```
