import { Component, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, Validators, ReactiveFormsModule, FormGroup } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AccountApiService } from '../../core/services/account-api.service';
import { CommonModule, NgIf } from '@angular/common';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, CommonModule, RouterLink, NgIf],
  templateUrl: './register.component.html',
})
export class RegisterComponent {
  loading = false;
  error = '';
  success = '';
  form: FormGroup;

  constructor(
    private fb: FormBuilder,
    private router: Router,
    private api: AccountApiService,
    private cdr: ChangeDetectorRef
  ) {
    this.form = this.fb.group({
      name: ['', [Validators.required]],
      email: ['', [Validators.required, Validators.pattern(/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/)]],
      password: ['', [Validators.required]],
      confirmPassword: ['', [Validators.required]],
    });
  }


  private extractApiError(err: any): string {
    const payload = err?.error;

    if (!payload) return 'Registration failed';

    if (typeof payload === 'string') {
      try {
        const parsed = JSON.parse(payload);
        return this.extractApiError({ error: parsed });
      } catch {
        return payload;
      }
    }

    if (payload?.errors && typeof payload.errors === 'object') {
      const msgs = Object.values(payload.errors)
        .flat()
        .filter((x) => typeof x === 'string') as string[];
      if (msgs.length) return msgs.join('\n');

      return payload.title ?? 'Validation error';
    }

    if (Array.isArray(payload)) {
      const descriptions = payload
        .map((x) => x?.description)
        .filter((x) => typeof x === 'string') as string[];
      if (descriptions.length) return descriptions.join('\n');

      const strings = payload.filter((x) => typeof x === 'string') as string[];
      if (strings.length) return strings.join('\n');

      return 'Registration failed';
    }

    if (typeof payload?.detail === 'string') return payload.detail;
    if (typeof payload?.title === 'string') return payload.title;
    if (typeof payload?.message === 'string') return payload.message;

    return 'Registration failed';
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { password, confirmPassword } = this.form.getRawValue();
    if (password !== confirmPassword) {
      this.error = 'Passwords do not match';
      this.success = '';
      this.cdr.detectChanges();
      return;
    }

    this.loading = true;
    this.error = '';
    this.success = '';
    this.cdr.detectChanges();

    const registerData = this.form.getRawValue();

    this.api.register(registerData).subscribe({
      next: (msg) => {
        this.loading = false;
        this.error = '';
        this.success = msg || 'Account created successfully. Redirecting to login...';
        this.form.disable();
        this.cdr.detectChanges();

        setTimeout(() => {
          this.router.navigateByUrl('/account/login');
        }, 1500);
      },
      error: (err) => {
        this.loading = false;
        this.success = '';
        this.error = this.extractApiError(err);
        this.cdr.detectChanges();
      },
    });
  }
}
