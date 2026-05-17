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
