import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, GridQuery } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { AboutInfo, AppSettings, Lookup, UserAccount, UserRole } from '../../core/models';
import { NotifyService, fieldErrorsOf, messageOf } from '../../core/notify.service';
import { LOOKUP_LABELS, LookupKind, ReferenceDataService } from '../../core/reference-data.service';
import { CellDirective, DataTableComponent, RowActionsDirective } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { EmployeePickerComponent } from '../../shared/employee-picker.component';
import { IconComponent } from '../../shared/icon.component';
import { ModalComponent } from '../../shared/modal.component';
import { EmptyStateComponent, ErrorStateComponent, FieldErrorComponent, LocalDatePipe, PageHeaderComponent } from '../../shared/ui';

type Tab = 'general' | 'master' | 'users' | 'about';

const PERMISSION_GROUPS: { title: string; items: string[] }[] = [
  { title: 'Dashboard & reports', items: ['Dashboard.View', 'Reports.View', 'ActivityLogs.View'] },
  { title: 'Assets', items: ['Assets.View', 'Assets.Create', 'Assets.Edit', 'Assets.Assign', 'Assets.Return', 'Assets.Repair', 'Assets.Scrap'] },
  { title: 'Inventory', items: ['Inventory.View', 'Inventory.StockIn', 'Inventory.StockOut'] },
  { title: 'Employees', items: ['Employees.View', 'Employees.Create', 'Employees.Edit'] },
  { title: 'Requests', items: ['Requests.View', 'Requests.Create', 'Requests.Approve', 'Requests.Process', 'Requests.Complete'] },
  { title: 'Administration', items: ['Users.Manage', 'Settings.Manage'] }
];

@Component({
  selector: 'app-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormsModule, DataTableComponent, CellDirective, RowActionsDirective, EmployeePickerComponent, IconComponent,
    ModalComponent, EmptyStateComponent, ErrorStateComponent, FieldErrorComponent, LocalDatePipe, PageHeaderComponent],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.css'
})
export class SettingsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  private readonly ref = inject(ReferenceDataService);
  private readonly fb = inject(FormBuilder);
  protected readonly auth = inject(AuthService);

  private readonly usersTable = viewChild<DataTableComponent>('usersTable');

  protected readonly tab = signal<Tab>(this.auth.can('Settings.Manage') ? 'general' : 'users');
  protected readonly canSettings = computed(() => this.auth.can('Settings.Manage'));
  protected readonly canUsers = computed(() => this.auth.can('Users.Manage'));

  // ---- General ----
  protected readonly savingSettings = signal(false);
  protected readonly settingsForm = this.fb.nonNullable.group({
    companyName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    currency: ['USD', [Validators.required, Validators.pattern(/^[A-Z]{3}$/)]],
    defaultPageSize: [10, [Validators.required, Validators.min(5), Validators.max(100)]],
    warrantyAlertDays: [30, [Validators.required, Validators.min(1), Validators.max(365)]]
  });

  // ---- Master data ----
  protected readonly kinds = Object.entries(LOOKUP_LABELS) as [LookupKind, string][];
  protected readonly kind = signal<LookupKind>('departments');
  protected readonly kindLabel = (k: LookupKind) => LOOKUP_LABELS[k];
  protected readonly lookups = signal<Lookup[] | null>(null);
  protected readonly lookupError = signal(false);
  protected readonly lookupEdit = signal<Lookup | null>(null);
  protected readonly lookupOpen = signal(false);
  protected readonly lookupSaving = signal(false);
  protected readonly lookupError2 = signal<string | null>(null);
  protected lookupName = '';
  protected lookupPrefix = '';
  protected lookupActive = true;

  // ---- Users ----
  protected readonly permissionGroups = PERMISSION_GROUPS;
  protected readonly userColumns: TableColumn[] = [
    { key: 'fullName', label: 'User', locked: true },
    { key: 'email', label: 'Email', hidden: true },
    { key: 'employeeCode', label: 'Employee', format: 'mono' },
    { key: 'role', label: 'Role', format: 'badge', options: [{ value: 'Admin', label: 'Administrator' }, { value: 'User', label: 'User' }] },
    { key: 'isActive', label: 'Active', format: 'bool' },
    { key: 'lastLoginAt', label: 'Last sign-in', format: 'datetime' },
    { key: 'createdAt', label: 'Created', format: 'date', hidden: true }
  ];
  protected readonly fetchUsers = (q: GridQuery) => this.api.grid<UserAccount>('users', q);
  protected readonly userEdit = signal<UserAccount | null>(null);
  protected readonly userOpen = signal(false);
  protected readonly userSaving = signal(false);
  protected readonly userRole = signal<UserRole>('User');
  protected readonly userActive = signal(true);
  protected readonly userPerms = signal<Set<string>>(new Set());
  protected readonly newUserOpen = signal(false);
  protected readonly newUserErrors = signal<Record<string, string>>({});
  protected readonly newUserForm = this.fb.group({
    employeeId: [null as number | null, Validators.required],
    role: ['User' as UserRole],
    password: ['', [Validators.required, Validators.minLength(8)]]
  });

  // ---- About ----
  protected readonly about = signal<AboutInfo | null>(null);

  ngOnInit(): void {
    if (this.canSettings()) {
      this.api.get<AppSettings>('settings').subscribe(s => this.settingsForm.reset(s));
      this.loadLookups();
    }
    this.api.get<AboutInfo>('settings/about').subscribe({ next: a => this.about.set(a), error: () => undefined });
  }

  // ---------- General ----------
  protected saveSettings(): void {
    if (this.savingSettings()) return;
    if (this.settingsForm.invalid) return this.settingsForm.markAllAsTouched();
    this.savingSettings.set(true);
    this.api.put<AppSettings>('settings', this.settingsForm.getRawValue()).subscribe({
      next: s => {
        this.savingSettings.set(false);
        this.ref.settings.set(s);
        this.notify.success('Settings saved.');
      },
      error: err => { this.savingSettings.set(false); this.notify.apiError(err); }
    });
  }

  // ---------- Master data ----------
  protected selectKind(kind: LookupKind): void {
    this.kind.set(kind);
    this.loadLookups();
  }

  protected loadLookups(): void {
    this.lookups.set(null);
    this.lookupError.set(false);
    this.api.get<Lookup[]>(`lookups/${this.kind()}`, { includeInactive: true }).subscribe({
      next: items => this.lookups.set(items),
      error: () => this.lookupError.set(true)
    });
  }

  protected openLookup(item: Lookup | null): void {
    this.lookupEdit.set(item);
    this.lookupName = item?.name ?? '';
    this.lookupPrefix = item?.tagPrefix ?? '';
    this.lookupActive = item?.isActive ?? true;
    this.lookupError2.set(null);
    this.lookupOpen.set(true);
  }

  protected saveLookup(): void {
    const name = this.lookupName.trim();
    if (!name || this.lookupSaving()) return;
    this.lookupSaving.set(true);
    const item = this.lookupEdit();
    const body = { name, tagPrefix: this.lookupPrefix.trim() || null, isActive: this.lookupActive };
    const req = item ? this.api.put<Lookup>(`lookups/${this.kind()}/${item.id}`, body) : this.api.post<Lookup>(`lookups/${this.kind()}`, body);
    req.subscribe({
      next: () => {
        this.lookupSaving.set(false);
        this.lookupOpen.set(false);
        this.ref.invalidate(this.kind());
        this.notify.success(`${LOOKUP_LABELS[this.kind()]} ${item ? 'updated' : 'added'}.`);
        this.loadLookups();
      },
      error: err => {
        this.lookupSaving.set(false);
        this.lookupError2.set(fieldErrorsOf(err)['name'] ?? messageOf(err));
      }
    });
  }

  protected async deleteLookup(item: Lookup): Promise<void> {
    const noun = LOOKUP_LABELS[this.kind()];
    if (!(await this.notify.confirm({ title: `Delete “${item.name}”?`, text: `This ${noun.toLowerCase()} is not used anywhere and will be removed.`, confirmText: 'Delete', danger: true }))) return;
    this.api.delete(`lookups/${this.kind()}/${item.id}`).subscribe({
      next: () => {
        this.ref.invalidate(this.kind());
        this.notify.success(`${noun} deleted.`);
        this.loadLookups();
      },
      error: err => this.notify.apiError(err, 'Cannot delete')
    });
  }

  // ---------- Users ----------
  protected async deleteUser(user: UserAccount): Promise<void> {
    const who = user.fullName ?? user.email;
    if (!(await this.notify.confirm({
      title: `Delete ${who}'s sign-in?`,
      text: 'They will lose access immediately. Their employee record stays.',
      confirmText: 'Delete sign-in',
      danger: true
    }))) return;
    this.api.delete(`users/${user.id}`).subscribe({
      next: () => {
        this.notify.success('Sign-in account deleted.');
        this.usersTable()?.reload();
      },
      error: err => this.notify.apiError(err, 'Cannot delete')
    });
  }

  protected openUser(user: UserAccount): void {
    this.userEdit.set(user);
    this.userRole.set(user.role);
    this.userActive.set(user.isActive);
    this.userPerms.set(new Set(user.grantedPermissions));
    this.userOpen.set(true);
  }

  protected togglePerm(p: string): void {
    const next = new Set(this.userPerms());
    if (next.has(p)) next.delete(p); else next.add(p);
    this.userPerms.set(next);
  }

  protected isDefault(p: string): boolean {
    return p === 'Requests.Create';
  }

  protected saveUser(): void {
    const user = this.userEdit();
    if (!user || this.userSaving()) return;
    this.userSaving.set(true);
    this.api.put<UserAccount>(`users/${user.id}`, { role: this.userRole(), isActive: this.userActive(), permissions: [...this.userPerms()] }).subscribe({
      next: () => {
        this.userSaving.set(false);
        this.userOpen.set(false);
        this.notify.success('User updated. They will need to sign in again for changes to apply.');
        this.usersTable()?.reload();
      },
      error: err => { this.userSaving.set(false); this.notify.apiError(err); }
    });
  }

  protected async resetPassword(user: UserAccount): Promise<void> {
    const password = await this.notify.prompt(`Reset password for ${user.fullName ?? user.email}`, 'New temporary password (8+ characters, letter and number)', true, 'Reset password');
    if (!password) return;
    this.api.post(`users/${user.id}/reset-password`, { password }).subscribe({
      next: () => this.notify.success('Password reset. Share it with the user securely.'),
      error: err => this.notify.apiError(err, 'Could not reset password')
    });
  }

  protected openNewUser(): void {
    this.newUserErrors.set({});
    this.newUserForm.reset({ employeeId: null, role: 'User', password: '' });
    this.newUserOpen.set(true);
  }

  protected createUser(): void {
    if (this.userSaving()) return;
    if (this.newUserForm.invalid) return this.newUserForm.markAllAsTouched();
    this.userSaving.set(true);
    this.newUserErrors.set({});
    this.api.post<UserAccount>('users', { ...this.newUserForm.getRawValue(), permissions: [] }).subscribe({
      next: u => {
        this.userSaving.set(false);
        this.newUserOpen.set(false);
        this.notify.success(`Sign-in created for ${u.fullName ?? u.email}.`);
        this.usersTable()?.reload();
      },
      error: err => {
        this.userSaving.set(false);
        const fields = fieldErrorsOf(err);
        this.newUserErrors.set(fields);
        if (Object.keys(fields).length === 0) this.notify.apiError(err);
      }
    });
  }
}
