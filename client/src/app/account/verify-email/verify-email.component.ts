import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { AccountApiService } from '../../core/services/account-api.service';

@Component({
  selector: 'app-verify-email',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './verify-email.component.html',
})
export class VerifyEmailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private api = inject(AccountApiService);

  message = signal<string | null>(null);
  error = signal<string | null>(null);

  ngOnInit() {
    const params = this.route.snapshot.queryParamMap;
    const email = params.get('email');
    const token = params.get('token');

    // A bare visit is not a verification attempt. Posting an empty token would earn a 400 that
    // tells the user nothing about what went wrong.
    if (!email || !token) {
      this.error.set('This verification link is incomplete. Use the link from your email.');
      return;
    }

    this.api
      .verifyEmail(email, token)
      .pipe(
        tap(msg => this.message.set(msg)),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'The address could not be verified.'));
          return of(null);
        }),
      )
      .subscribe();
  }
}
