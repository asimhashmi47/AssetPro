import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService, GridQuery } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { avatarClass, initials } from '../../core/labels';
import { Employee } from '../../core/models';
import { CellDirective, DataTableComponent, RowActionsDirective } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { IconComponent } from '../../shared/icon.component';
import { PageHeaderComponent } from '../../shared/ui';
import { EmployeeFormComponent } from './employee-form.component';

@Component({
  selector: 'app-employees-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DataTableComponent, CellDirective, RowActionsDirective, IconComponent, PageHeaderComponent, EmployeeFormComponent],
  template: `
    <div class="page">
      <app-page-header title="Employees" subtitle="People, their roles and the assets they hold." icon="users">
        @if (auth.can('Employees.Create')) {
          <button type="button" class="btn btn-primary" (click)="openForm(null)"><app-icon name="user-plus" [size]="16" /> Add employee</button>
        }
      </app-page-header>

      <app-data-table #table [columns]="columns" [fetch]="fetch" storageKey="employees" exportName="employees" defaultSort="fullName"
        searchPlaceholder="Search name, email, department…" emptyTitle="No employees available" emptyMessage="Add your first employee to start assigning assets." emptyIcon="users">
        <ng-template appCell="fullName" let-row>
          <a class="person" [routerLink]="['/employees', row.id]">
            <span class="avatar" [class]="'avatar ' + avatarClass(row.fullName)">{{ initials(row.fullName) }}</span>
            <span class="person-text"><strong>{{ row.fullName }}</strong><span>{{ row.email }}</span></span>
          </a>
        </ng-template>
        <ng-template appCell="role" let-row>
          @if (row.role) { <span class="badge plain" [class]="row.role === 'Admin' ? 'badge plain violet' : 'badge plain'">{{ row.role === 'Admin' ? 'Administrator' : 'User' }}</span> }
          @else { <span class="muted small">No sign-in</span> }
        </ng-template>
        <ng-template appCell="assignedAssets" let-row>
          <span class="count" [class.zero]="row.assignedAssets === 0">{{ row.assignedAssets }}</span>
        </ng-template>
        <ng-template appRowActions let-row>
          @if (auth.can('Employees.Edit')) {
            <button type="button" class="btn btn-sm btn-ghost btn-icon" (click)="openForm(row)" title="Edit" aria-label="Edit employee"><app-icon name="edit" [size]="16" /></button>
          }
          <a class="btn btn-sm btn-ghost btn-icon" [routerLink]="['/employees', row.id]" title="View profile" aria-label="View profile"><app-icon name="eye" [size]="16" /></a>
        </ng-template>
      </app-data-table>
    </div>
    <app-employee-form [open]="formOpen()" [employee]="editing()" (closed)="formOpen.set(false)" (saved)="onSaved($event)" />
  `,
  styles: `
    .person { display: flex; align-items: center; gap: 10px; color: inherit; min-width: 200px; }
    .person:hover { text-decoration: none; }
    .person:hover strong { color: var(--primary-strong); }
    .person-text { display: flex; flex-direction: column; min-width: 0; }
    .person-text strong { color: var(--ink); font-size: 13.5px; max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .person-text span { font-size: 12px; color: var(--ink-3); max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .count { display: inline-grid; place-items: center; min-width: 28px; height: 24px; padding: 0 8px; border-radius: 8px; background: var(--primary-soft); color: var(--primary-ink); font-weight: 700; font-size: 12.5px; }
    .count.zero { background: var(--surface-3); color: var(--ink-4); }
  `
})
export class EmployeesListComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  private readonly table = viewChild.required<DataTableComponent>('table');

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Employee | null>(null);
  protected readonly initials = initials;
  protected readonly avatarClass = avatarClass;

  protected readonly columns: TableColumn[] = [
    { key: 'fullName', label: 'Employee', locked: true },
    { key: 'employeeCode', label: 'Code', format: 'mono' },
    { key: 'email', label: 'Email', hidden: true },
    { key: 'department', label: 'Department' },
    { key: 'designation', label: 'Designation' },
    { key: 'contactNumber', label: 'Contact', hidden: true },
    { key: 'companyName', label: 'Company', hidden: true },
    { key: 'role', label: 'Role', format: 'badge', options: [{ value: 'Admin', label: 'Administrator' }, { value: 'User', label: 'User' }] },
    { key: 'assignedAssets', label: 'Assets', format: 'number', align: 'center' },
    { key: 'status', label: 'Status', format: 'badge', options: [{ value: 'Active', label: 'Active' }, { value: 'Inactive', label: 'Inactive' }] },
    { key: 'createdAt', label: 'Created', format: 'date', hidden: true }
  ];

  protected readonly fetch = (q: GridQuery) => this.api.grid<Employee>('employees', q);

  protected openForm(employee: Employee | null): void {
    this.editing.set(employee);
    this.formOpen.set(true);
  }

  protected onSaved(employee: Employee): void {
    const wasNew = !this.editing();
    this.formOpen.set(false);
    if (wasNew) this.router.navigate(['/employees', employee.id]);
    else this.table().reload();
  }
}
