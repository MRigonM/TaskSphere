import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, of, tap } from 'rxjs';

import { AccountApiService } from '../../core/services/account-api.service';

const NEUTRAL_ANSWER = 'If that address has an account, a password reset link is on its way.';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.component.html',
})
export class ForgotPasswordComponent {
  private api = inject(AccountApiService);
  private fb = inject(FormBuilder);

  loading = signal(false);
  message = signal<string | null>(null);

  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  submit() {
    if (this.form.invalid) return;

    this.loading.set(true);

    this.api
      .forgotPassword(this.form.getRawValue().email)
      .pipe(
        tap(msg => {
          this.message.set(msg);
          this.loading.set(false);
        }),
        // The server never reveals whether the address exists. Rendering a distinguishable
        // failure here would give away exactly what it withheld, so every outcome shows the
        // same sentence.
        catchError(() => {
          this.message.set(NEUTRAL_ANSWER);
          this.loading.set(false);
          return of(null);
        }),
      )
      .subscribe();
  }
}
