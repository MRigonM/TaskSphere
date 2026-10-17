import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { apiErrorMessage } from '../../core/http/api-error';
import { AgentStatusDto, IssuedAgentTokenDto } from '../../core/models/agents.models';
import { AgentsApiService } from '../../core/services/agents-api.service';

@Component({
  selector: 'app-my-claude',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './my-claude.component.html',
})
export class MyClaudeComponent implements OnInit {
  status = signal<AgentStatusDto | null>(null);
  /** Held in memory only: the server can never show this token again. */
  issued = signal<IssuedAgentTokenDto | null>(null);
  busy = signal(false);
  error = signal<string | null>(null);

  readonly mcpUrl = environment.mcpUrl;

  constructor(private api: AgentsApiService) {}

  ngOnInit() {
    this.load();
  }

  command(token: string): string {
    return `claude mcp add --transport http tasksphere ${this.mcpUrl} --header "Authorization: Bearer ${token}"`;
  }

  create() {
    this.run(this.api.create(), () => this.load());
  }

  regenerate() {
    if (this.status()?.token && !window.confirm('The current token stops working immediately. Continue?')) return;
    this.run(this.api.regenerateToken(), (issued) => {
      this.issued.set(issued);
      this.load();
    });
  }

  revoke() {
    if (!window.confirm('Claude will lose access until you generate a new token. Continue?')) return;
    this.run(this.api.revokeToken(), () => {
      this.issued.set(null);
      this.load();
    });
  }

  private load() {
    this.api.getMine().subscribe({
      next: (s) => this.status.set(s),
      error: (err) => this.error.set(apiErrorMessage(err, 'Could not load your Claude.')),
    });
  }

  private run<T>(call: import('rxjs').Observable<T>, done: (value: T) => void) {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: (value) => {
        this.busy.set(false);
        done(value);
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(apiErrorMessage(err, 'Something went wrong.'));
      },
    });
  }
}
