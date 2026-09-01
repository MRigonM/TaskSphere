import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, of, tap } from 'rxjs';

import { apiErrorMessage } from '../../core/http/api-error';
import { AccountApiService } from '../../core/services/account-api.service';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
})
export class ResetPasswordComponent {
  private route = inject(ActivatedRoute);
  private api = inject(AccountApiService);
  private fb = inject(FormBuilder);

  private email = this.route.snapshot.queryParamMap.get('email') ?? '';
  private token = this.route.snapshot.queryParamMap.get('token') ?? '';

  incomplete = !this.email || !this.token;

  loading = signal(false);
  message = signal<string | null>(null);
  error = signal<string | null>(null);

  form = this.fb.nonNullable.group({
    password: ['', [Validators.required]],
    confirmPassword: ['', [Validators.required]],
  });

  submit() {
    if (this.incomplete || this.form.invalid) return;

    const { password, confirmPassword } = this.form.getRawValue();
    if (password !== confirmPassword) {
      this.error.set('The two passwords do not match.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api
      .resetPassword({ email: this.email, token: this.token, password, confirmPassword })
      .pipe(
        tap(msg => {
          this.message.set(msg);
          this.loading.set(false);
        }),
        catchError(err => {
          this.error.set(apiErrorMessage(err, 'Your password could not be changed.'));
          this.loading.set(false);
          return of(null);
        }),
      )
      .subscribe();
  }
}
