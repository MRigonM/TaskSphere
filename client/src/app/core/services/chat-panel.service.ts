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
