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
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }
}
