import { Component, ChangeDetectorRef } from '@angular/core';
import {FormBuilder, Validators, ReactiveFormsModule, FormGroup} from '@angular/forms';
import {Router, RouterLink} from '@angular/router';
import { AccountApiService } from '../../core/services/account-api.service';
import { AuthStoreService } from '../../core/services/auth-store.service';
import {CommonModule, NgIf} from '@angular/common';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, CommonModule, RouterLink, NgIf],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  loading = false;
  error = '';
  unconfirmed = false;
  resendNotice = '';
  form: FormGroup;
  constructor(
    private fb: FormBuilder,
    private router: Router,
    private api: AccountApiService,
    private auth: AuthStoreService,
    private cdr: ChangeDetectorRef
  ){
    this.form = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]],
    });
  }

  private extractApiError(err: any): string {
    const payload = err?.error;

    if (!payload) return 'Login failed';

    if (Array.isArray(payload)) {
      const msgs = payload
        .map((x) => x?.description)
        .filter((x) => typeof x === 'string');
      if (msgs.length) return msgs.join('\n');
    }

    if (typeof payload === 'string') return payload;
    if (payload?.message) return payload.message;

    return 'Login failed';
  }

  /**
   * Keyed off the error CODE, never the message text: the wording is a server-side string that
   * can change, and matching on it would make the button appear or vanish with a copy edit.
   */
  private isUnconfirmed(err: any): boolean {
    return Array.isArray(err?.error)
      && err.error.some((e: any) => e?.code === 'Auth.EmailNotConfirmed');
  }

  resendVerification() {
    const { email } = this.form.getRawValue();
    this.resendNotice = '';
    this.cdr.detectChanges();

    this.api.resendVerification(email).subscribe({
      next: (msg) => { this.resendNotice = msg; this.cdr.detectChanges(); },
      error: () => {
        this.resendNotice = 'Could not request another email. Try again shortly.';
        this.cdr.detectChanges();
      },
    });
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading = true;
    this.error = '';

    const loginData = this.form.getRawValue();

    this.api.login(loginData).subscribe({
      next: (res) => {
        this.auth.setAuth(res);
        this.loading = false;
        this.cdr.detectChanges();
        this.router.navigateByUrl('/');
      },
      error: (err) => {
        this.error = this.extractApiError(err);
        this.unconfirmed = this.isUnconfirmed(err);
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }
}
