import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiService, GridQuery } from '../../core/api.service';
import { ActivityLog, ApplicationLog } from '../../core/models';
import { CellDirective, DataTableComponent } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { IconComponent } from '../../shared/icon.component';
import { BadgeComponent, PageHeaderComponent } from '../../shared/ui';

const ACTION_OPTIONS = ['Login', 'LoginFailed', 'Logout', 'UserCreated', 'UserUpdated', 'EmployeeCreated', 'EmployeeUpdated', 'AssetCreated', 'AssetUpdated',
  'AssetAssigned', 'AssetReturned', 'AssetRepaired', 'AssetStatusChanged', 'ComponentReplaced', 'AssetScrapped', 'InventoryAdded', 'InventoryAdjusted',
  'RequestCreated', 'RequestApproved', 'RequestRejected', 'RequestStatusChanged', 'MasterDataChanged', 'SettingsUpdated']
  .map(v => ({ value: v, label: v.replace(/([a-z])([A-Z])/g, '$1 $2') }));

@Component({
  selector: 'app-activity-logs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DataTableComponent, CellDirective, IconComponent, BadgeComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header title="Activity Logs" subtitle="Who did what, and when. Passwords and tokens are never logged." icon="activity" />
      <div class="tabs" role="tablist">
        <button type="button" role="tab" [class.active]="tab() === 'activity'" (click)="tab.set('activity')"><app-icon name="user" [size]="15" /> User activity</button>
        <button type="button" role="tab" [class.active]="tab() === 'app'" (click)="tab.set('app')"><app-icon name="database" [size]="15" /> Application log</button>
      </div>

      @if (tab() === 'activity') {
        <app-data-table [columns]="activityColumns" [fetch]="fetchActivity" storageKey="activity" exportName="activity-log" defaultSort="occurredAt" defaultDir="desc"
          searchPlaceholder="Search user, action, description…" emptyTitle="No activity recorded yet" emptyIcon="activity">
          <ng-template appCell="action" let-row>
            <span class="action"><span class="a-dot" [class]="'a-dot ' + tone(row.action)"></span>{{ pretty(row.action) }}</span>
          </ng-template>
          <ng-template appCell="userName" let-row>
            <span class="who"><strong>{{ row.userName ?? row.userEmail ?? 'System' }}</strong>@if (row.userName && row.userEmail) { <span>{{ row.userEmail }}</span> }</span>
          </ng-template>
        </app-data-table>
      } @else {
        <app-data-table [columns]="appColumns" [fetch]="fetchApp" storageKey="app-log" exportName="application-log" defaultSort="timestamp" defaultDir="desc"
          searchPlaceholder="Search messages…" emptyTitle="No application log entries" emptyMessage="Warnings and errors will be recorded here." emptyIcon="database">
          <ng-template appCell="level" let-row><app-badge [value]="row.level" /></ng-template>
          <ng-template appCell="message" let-row>
            <span class="msg" [title]="row.exception ?? row.message">{{ row.message }}</span>
          </ng-template>
        </app-data-table>
      }
    </div>`,
  styles: `
    .action { display: inline-flex; align-items: center; gap: 8px; font-weight: 600; color: var(--ink); white-space: nowrap; }
    .a-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ink-4); }
    .a-dot.success { background: var(--success); } .a-dot.danger { background: var(--danger); } .a-dot.primary { background: var(--primary); }
    .a-dot.warning { background: var(--warning); } .a-dot.violet { background: var(--violet); }
    .who { display: flex; flex-direction: column; }
    .who strong { font-size: 13px; color: var(--ink); max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .who span { font-size: 11.5px; color: var(--ink-3); max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .msg { display: inline-block; max-width: 520px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; vertical-align: bottom; }
  `
})
export class ActivityLogsComponent {
  private readonly api = inject(ApiService);
  protected readonly tab = signal<'activity' | 'app'>('activity');

  protected readonly activityColumns: TableColumn[] = [
    { key: 'occurredAt', label: 'When', format: 'datetime' },
    { key: 'userName', label: 'User' },
    { key: 'action', label: 'Action', format: 'badge', options: ACTION_OPTIONS },
    { key: 'description', label: 'Description' },
    { key: 'entity', label: 'Entity' },
    { key: 'entityId', label: 'Entity ID', hidden: true },
    { key: 'ipAddress', label: 'IP address', format: 'mono', hidden: true },
    { key: 'result', label: 'Result', format: 'badge', options: [{ value: 'Success', label: 'Success' }, { value: 'Failure', label: 'Failure' }] }
  ];

  protected readonly appColumns: TableColumn[] = [
    { key: 'timestamp', label: 'When', format: 'datetime' },
    { key: 'level', label: 'Level', format: 'badge', options: ['Information', 'Warning', 'Error', 'Critical'].map(v => ({ value: v, label: v })) },
    { key: 'message', label: 'Message' },
    { key: 'category', label: 'Source', hidden: true },
    { key: 'requestPath', label: 'Path', format: 'mono' },
    { key: 'traceId', label: 'Trace ID', format: 'mono', hidden: true }
  ];

  protected readonly fetchActivity = (q: GridQuery) => this.api.grid<ActivityLog>('activity-logs', q);
  protected readonly fetchApp = (q: GridQuery) => this.api.grid<ApplicationLog>('activity-logs/application', q);

  protected pretty(action: string): string {
    return action.replace(/([a-z])([A-Z])/g, '$1 $2');
  }

  protected tone(action: string): string {
    if (action.includes('Failed') || action.includes('Rejected') || action.includes('Scrapped')) return 'danger';
    if (action.startsWith('Login') || action.includes('Created') || action.includes('Added')) return 'success';
    if (action.includes('Assigned') || action.includes('Approved')) return 'primary';
    if (action.includes('Repair') || action.includes('Returned')) return 'warning';
    if (action.includes('Request') || action.includes('Component')) return 'violet';
    return '';
  }
}
