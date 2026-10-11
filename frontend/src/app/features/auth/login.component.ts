import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { NotifyService, messageOf } from '../../core/notify.service';
import { CreditComponent } from '../../shared/credit.component';
import { IconComponent } from '../../shared/icon.component';
import { FieldErrorComponent } from '../../shared/ui';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, IconComponent, FieldErrorComponent, CreditComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);

  /** Bound from the query string by the router. */
  readonly returnUrl = input<string>();
  readonly expired = input<string>();

  protected readonly showPassword = signal(false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
    rememberMe: [false]
  });

  protected readonly features = [
    { icon: 'user-check', title: 'Know who has what', text: 'Every laptop, monitor and headset — and who holds it right now.' },
    { icon: 'history', title: 'A lifetime of history', text: 'Assignments, returns, repairs and part swaps on one timeline.' },
    { icon: 'inbox', title: 'Requests that flow', text: 'From submitted to done, with every hand-off timestamped.' }
  ];

  protected fillDemo(email: string, password: string): void {
    this.form.patchValue({ email, password });
    this.error.set(null);
  }

  protected submit(): void {
    if (this.submitting()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    const { email, password, rememberMe } = this.form.getRawValue();

    this.auth.login(email.trim(), password, rememberMe).subscribe({
      next: response => {
        this.notify.success(`Welcome back, ${response.user.fullName.split(' ')[0]}!`);
        const target = this.safeReturnUrl() ?? this.auth.homeUrl();
        this.router.navigateByUrl(target);
      },
      error: err => {
        this.submitting.set(false);
        this.error.set(messageOf(err));
        this.form.controls.password.reset('');
      }
    });
  }

  /** Only allow in-app relative paths to avoid open redirects. */
  private safeReturnUrl(): string | null {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login') ? url : null;
  }
}
