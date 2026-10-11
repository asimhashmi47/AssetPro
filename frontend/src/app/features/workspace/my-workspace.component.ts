import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { EmployeeProfile, RequestDetail } from '../../core/models';
import { EmptyStateComponent, ErrorStateComponent } from '../../shared/ui';
import { ProfileViewComponent } from '../employees/profile-view.component';
import { RequestFormComponent } from '../requests/request-form.component';

/** The standard user's home: own profile, assets, history and requests. */
@Component({
  selector: 'app-my-workspace',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProfileViewComponent, RequestFormComponent, EmptyStateComponent, ErrorStateComponent],
  template: `
    <div class="page">
      @if (missing()) {
        <div class="card">
          <app-empty-state title="No employee profile linked" icon="user"
            message="Your sign-in isn’t linked to an employee record yet. Ask an administrator to link it so you can see your assets." />
        </div>
      } @else if (failed()) {
        <div class="card"><app-error-state (retry)="load()" /></div>
      } @else if (profile(); as p) {
        <app-profile-view [profile]="p" [self]="true" (newRequest)="openRequest($event)" />
      } @else {
        <div class="skeleton" style="height: 220px; border-radius: 16px"></div>
        <div class="grid grid-2"><div class="skeleton" style="height: 300px"></div><div class="skeleton" style="height: 300px"></div></div>
      }
    </div>
    <app-request-form [open]="requestOpen()" [presetAssetId]="presetAsset()" (closed)="requestOpen.set(false)" (created)="onCreated($event)" />
  `
})
export class MyWorkspaceComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly profile = signal<EmployeeProfile | null>(null);
  protected readonly missing = signal(false);
  protected readonly failed = signal(false);
  protected readonly requestOpen = signal(false);
  protected readonly presetAsset = signal<number | null>(null);

  ngOnInit(): void {
    if (!this.auth.user()?.employeeId) {
      this.missing.set(true);
      return;
    }
    this.load();
  }

  protected load(): void {
    this.failed.set(false);
    this.api.get<EmployeeProfile>('me/profile').subscribe({
      next: p => this.profile.set(p),
      error: err => (err?.status === 404 ? this.missing.set(true) : this.failed.set(true))
    });
  }

  protected openRequest(assetId: number | null): void {
    this.presetAsset.set(assetId);
    this.requestOpen.set(true);
  }

  protected onCreated(detail: RequestDetail): void {
    this.requestOpen.set(false);
    this.router.navigate(['/requests', detail.request.id]);
  }
}
