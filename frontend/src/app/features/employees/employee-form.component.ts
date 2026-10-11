import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Employee, EmployeeStatus, UserRole } from '../../core/models';
import { NotifyService, fieldErrorsOf } from '../../core/notify.service';
import { clearServerErrorsOnChange } from '../../core/forms';
import { ModalComponent } from '../../shared/modal.component';
import { SmartSelectComponent } from '../../shared/smart-select.component';
import { FieldErrorComponent } from '../../shared/ui';

@Component({
  selector: 'app-employee-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ModalComponent, SmartSelectComponent, FieldErrorComponent],
  template: `
    <app-modal [open]="open()" [title]="employee() ? 'Edit employee' : 'Add employee'" [subtitle]="employee()?.employeeCode ?? 'Create a new employee profile'"
      [icon]="employee() ? 'edit' : 'user-plus'" size="lg" [busy]="saving()" (closed)="closed.emit()">
      <form id="employee-form" class="form-grid" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <div class="field">
          <label for="e-name">Full name<span class="req">*</span></label>
          <input id="e-name" class="input" formControlName="fullName" maxlength="150" autocomplete="off" />
          <app-field-error [control]="form.controls.fullName" name="Full name" [server]="errors()['fullName']" />
        </div>
        <div class="field">
          <label for="e-email">Email<span class="req">*</span></label>
          <input id="e-email" class="input" type="email" formControlName="email" maxlength="200" autocomplete="off" />
          <app-field-error [control]="form.controls.email" name="Email" [server]="errors()['email']" />
        </div>
        <div class="field">
          <label for="e-phone">Contact number</label>
          <input id="e-phone" class="input" type="tel" formControlName="contactNumber" maxlength="30" placeholder="+1 555 123 4567" />
          <app-field-error [control]="form.controls.contactNumber" name="Contact number" [server]="errors()['contactNumber']" />
        </div>
        <div class="field">
          <label for="e-company">Company</label>
          <input id="e-company" class="input" formControlName="companyName" maxlength="150" />
        </div>
        <div class="field">
          <label for="e-dept">Department</label>
          <app-smart-select inputId="e-dept" kind="departments" formControlName="departmentId" placeholder="Select department" />
        </div>
        <div class="field">
          <label for="e-title">Designation</label>
          <app-smart-select inputId="e-title" kind="designations" formControlName="designationId" placeholder="Select designation" />
        </div>
        @if (employee()) {
          <div class="field">
            <label for="e-status">Status</label>
            <select id="e-status" class="select" formControlName="status">
              <option value="Active">Active</option>
              <option value="Inactive">Inactive</option>
            </select>
            <span class="hint">Employees holding assets cannot be deactivated.</span>
          </div>
        }

        @if (!employee() && auth.can('Users.Manage')) {
          <div class="full login-box" [formGroup]="form">
            <label class="check"><input type="checkbox" formControlName="createLogin" /> Create a sign-in account for this employee</label>
            @if (form.controls.createLogin.value) {
              <div class="form-grid" style="margin-top: 14px">
                <div class="field">
                  <label for="e-role">Role</label>
                  <select id="e-role" class="select" formControlName="role">
                    <option value="User">Standard user</option>
                    <option value="Admin">Administrator</option>
                  </select>
                </div>
                <div class="field">
                  <label for="e-pass">Temporary password<span class="req">*</span></label>
                  <input id="e-pass" class="input" type="password" formControlName="password" autocomplete="new-password" />
                  <span class="hint">8+ characters with a letter and a number.</span>
                  <app-field-error [server]="errors()['login.password'] || errors()['password']" />
                </div>
              </div>
            }
          </div>
        }
      </form>
      <ng-container modal-footer>
        <button type="button" class="btn" (click)="closed.emit()" [disabled]="saving()">Cancel</button>
        <button type="submit" form="employee-form" class="btn btn-primary" [disabled]="saving()">
          @if (saving()) { <span class="spinner"></span> } {{ employee() ? 'Save changes' : 'Create employee' }}
        </button>
      </ng-container>
    </app-modal>`,
  styles: `.login-box { padding: 14px 16px; border-radius: 12px; background: var(--surface-2); border: 1px dashed var(--line-strong); }`
})
export class EmployeeFormComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  protected readonly auth = inject(AuthService);

  readonly open = input(false);
  readonly employee = input<Employee | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Employee>();

  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string>>({});

  protected readonly form = inject(FormBuilder).group({
    fullName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]],
    contactNumber: ['', [Validators.maxLength(30), Validators.pattern(/^[0-9+()\-\s]*$/)]],
    companyName: ['', Validators.maxLength(150)],
    departmentId: [null as number | null],
    designationId: [null as number | null],
    status: ['Active' as EmployeeStatus],
    createLogin: [false],
    role: ['User' as UserRole],
    password: ['']
  });

  constructor() {
    clearServerErrorsOnChange(this.form, this.errors, inject(DestroyRef));
    effect(() => {
      if (!this.open()) return;
      this.errors.set({});
      const e = this.employee();
      this.form.reset({
        fullName: e?.fullName ?? '', email: e?.email ?? '', contactNumber: e?.contactNumber ?? '', companyName: e?.companyName ?? '',
        departmentId: e?.departmentId ?? null, designationId: e?.designationId ?? null, status: e?.status ?? 'Active',
        createLogin: false, role: 'User', password: ''
      });
    });
  }

  protected save(): void {
    if (this.saving()) return;
    if (this.form.invalid) return this.form.markAllAsTouched();
    const v = this.form.getRawValue();
    if (v.createLogin && (v.password ?? '').length < 8) {
      this.errors.set({ password: 'Enter a temporary password of at least 8 characters.' });
      return;
    }
    this.saving.set(true);
    this.errors.set({});
    const body = {
      fullName: v.fullName?.trim(), email: v.email?.trim(), contactNumber: v.contactNumber?.trim() || null, companyName: v.companyName?.trim() || null,
      departmentId: v.departmentId, designationId: v.designationId, status: v.status,
      login: !this.employee() && v.createLogin ? { role: v.role, password: v.password } : null
    };
    const e = this.employee();
    const request = e ? this.api.put<Employee>(`employees/${e.id}`, body) : this.api.post<Employee>('employees', body);
    request.subscribe({
      next: saved => {
        this.saving.set(false);
        this.notify.success(e ? 'Employee updated successfully.' : `${saved.fullName} added (${saved.employeeCode}).`);
        this.saved.emit(saved);
      },
      error: err => {
        this.saving.set(false);
        const fields = fieldErrorsOf(err);
        this.errors.set(fields);
        if (Object.keys(fields).length === 0) this.notify.apiError(err);
      }
    });
  }
}
