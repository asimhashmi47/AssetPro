import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { NotifyService, fieldErrorsOf } from '../../core/notify.service';
import { clearServerErrorsOnChange } from '../../core/forms';
import { FieldErrorComponent, PageHeaderComponent } from '../../shared/ui';

const matchPasswords = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

@Component({
  selector: 'app-account',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, PageHeaderComponent, FieldErrorComponent],
  template: `
    <div class="page">
      <app-page-header title="Account security" subtitle="Change the password you use to sign in to AssetPro." icon="key" />
      <form class="card card-pad stack narrow" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <div class="field">
          <label for="current">Current password<span class="req">*</span></label>
          <input id="current" class="input" type="password" formControlName="currentPassword" autocomplete="current-password" />
          <app-field-error [control]="form.controls.currentPassword" name="Current password" [server]="serverErrors()['currentPassword']" />
        </div>
        <div class="field">
          <label for="new">New password<span class="req">*</span></label>
          <input id="new" class="input" type="password" formControlName="newPassword" autocomplete="new-password" />
          <span class="hint">At least 8 characters, including a letter and a number.</span>
          <app-field-error [control]="form.controls.newPassword" name="New password" [server]="serverErrors()['newPassword']" />
        </div>
        <div class="field">
          <label for="confirm">Confirm new password<span class="req">*</span></label>
          <input id="confirm" class="input" type="password" formControlName="confirm" autocomplete="new-password" />
          @if (form.hasError('mismatch') && form.controls.confirm.touched) { <span class="error">Passwords do not match.</span> }
        </div>
        <div class="row">
          <span class="muted small">You’ll be signed out after changing it.</span>
          <span class="spacer"></span>
          <button type="submit" class="btn btn-primary" [disabled]="saving()">
            @if (saving()) { <span class="spinner"></span> } Update password
          </button>
        </div>
      </form>
    </div>`,
  styles: `.narrow { max-width: 520px; }`
})
export class AccountComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  protected readonly saving = signal(false);
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128), Validators.pattern(/^(?=.*[A-Za-z])(?=.*\d).+$/)]],
    confirm: ['', Validators.required]
  }, { validators: matchPasswords });

  constructor() {
    clearServerErrorsOnChange(this.form, this.serverErrors, inject(DestroyRef));
  }

  protected save(): void {
    if (this.saving()) return;
    if (this.form.invalid) return this.form.markAllAsTouched();
    this.saving.set(true);
    this.serverErrors.set({});
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.api.post('auth/change-password', { currentPassword, newPassword }).subscribe({
      next: () => {
        this.notify.success('Password updated. Please sign in again.');
        this.auth.logout();
      },
      error: err => {
        this.saving.set(false);
        const fields = fieldErrorsOf(err);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.notify.apiError(err);
      }
    });
  }
}
