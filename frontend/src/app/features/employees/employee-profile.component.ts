import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { EmployeeProfile } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { IconComponent } from '../../shared/icon.component';
import { EmptyStateComponent, ErrorStateComponent } from '../../shared/ui';
import { EmployeeFormComponent } from './employee-form.component';
import { ProfileViewComponent } from './profile-view.component';

@Component({
  selector: 'app-employee-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, EmptyStateComponent, ErrorStateComponent, EmployeeFormComponent, ProfileViewComponent],
  template: `
    <div class="page">
      <a class="back" routerLink="/employees"><app-icon name="arrow-left" [size]="15" /> All employees</a>
      @if (error() === 'missing') {
        <div class="card"><app-empty-state title="Employee not found" icon="search" message="This employee does not exist." /></div>
      } @else if (error() === 'failed') {
        <div class="card"><app-error-state (retry)="load()" /></div>
      } @else if (profile(); as p) {
        <app-profile-view [profile]="p" (edit)="editOpen.set(true)" (remove)="remove(p)" />
        <app-employee-form [open]="editOpen()" [employee]="p.employee" (closed)="editOpen.set(false)" (saved)="editOpen.set(false); load()" />
      } @else {
        <div class="skeleton" style="height: 220px; border-radius: 16px"></div>
        <div class="grid grid-2"><div class="skeleton" style="height: 300px"></div><div class="skeleton" style="height: 300px"></div></div>
      }
    </div>`,
  styles: `.back { display: inline-flex; align-items: center; gap: 6px; color: var(--ink-3); font-weight: 600; font-size: 13px; width: fit-content; }
    .back:hover { color: var(--ink); text-decoration: none; }`
})
export class EmployeeProfileComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  readonly id = input.required<string>();

  protected readonly profile = signal<EmployeeProfile | null>(null);
  protected readonly error = signal<'missing' | 'failed' | null>(null);
  protected readonly editOpen = signal(false);

  constructor() {
    effect(() => this.load(this.id()));
  }

  protected load(id = this.id()): void {
    this.error.set(null);
    this.api.get<EmployeeProfile>(`employees/${id}`).subscribe({
      next: p => this.profile.set(p),
      error: err => this.error.set(err?.status === 404 ? 'missing' : 'failed')
    });
  }

  protected async remove(p: EmployeeProfile): Promise<void> {
    const e = p.employee;
    const ok = await this.notify.confirm({
      title: `Delete ${e.fullName}?`,
      text: 'Their profile, sign-in account and requests will be removed permanently. This cannot be undone.',
      confirmText: 'Delete permanently',
      danger: true
    });
    if (!ok) return;
    this.api.delete(`employees/${e.id}`).subscribe({
      next: () => {
        this.notify.success(`${e.fullName} deleted.`);
        this.router.navigate(['/employees']);
      },
      error: err => this.notify.apiError(err, 'Could not delete the employee')
    });
  }
}
