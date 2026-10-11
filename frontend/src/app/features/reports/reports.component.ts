import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, GridQuery } from '../../core/api.service';
import { ASSET_ACTIONS, ASSET_CONDITIONS, ASSET_STATUSES, REQUEST_STATUSES, REQUEST_TYPES } from '../../core/labels';
import { CellDirective, DataTableComponent } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { IconComponent } from '../../shared/icon.component';
import { PageHeaderComponent } from '../../shared/ui';

interface ReportDef {
  key: string;
  title: string;
  description: string;
  icon: string;
  endpoint: string;
  defaultSort: string;
  defaultDir: 'asc' | 'desc';
  columns: TableColumn[];
}

const REPORTS: ReportDef[] = [
  {
    key: 'assets', title: 'Asset inventory', description: 'All assets with status, location, cost and holder.', icon: 'box',
    endpoint: 'reports/assets', defaultSort: 'assetTag', defaultDir: 'asc',
    columns: [
      { key: 'assetTag', label: 'Tag', format: 'mono' }, { key: 'name', label: 'Asset' }, { key: 'assetType', label: 'Type' },
      { key: 'brand', label: 'Brand' }, { key: 'model', label: 'Model', hidden: true }, { key: 'serialNumber', label: 'Serial', format: 'mono' },
      { key: 'status', label: 'Status', format: 'badge', options: ASSET_STATUSES }, { key: 'condition', label: 'Condition', format: 'badge', options: ASSET_CONDITIONS },
      { key: 'assignedTo', label: 'Holder' }, { key: 'location', label: 'Location' },
      { key: 'purchaseDate', label: 'Purchased', format: 'date' }, { key: 'purchaseCost', label: 'Cost', format: 'money', align: 'end' },
      { key: 'warrantyExpiry', label: 'Warranty', format: 'date' }
    ]
  },
  {
    key: 'employee-assets', title: 'Employee assets', description: 'Who holds what, including past assignments.', icon: 'users',
    endpoint: 'reports/employee-assets', defaultSort: 'assignedAt', defaultDir: 'desc',
    columns: [
      { key: 'employeeName', label: 'Employee' }, { key: 'employeeCode', label: 'Code', format: 'mono' }, { key: 'department', label: 'Department' },
      { key: 'assetTag', label: 'Asset', format: 'mono' }, { key: 'assetName', label: 'Asset name' }, { key: 'assetType', label: 'Type' },
      { key: 'assignedAt', label: 'Assigned', format: 'date' }, { key: 'returnedAt', label: 'Returned', format: 'date' },
      { key: 'isReturned', label: 'Returned?', format: 'bool' },
      { key: 'conditionAtAssignment', label: 'Condition out', format: 'badge', options: ASSET_CONDITIONS, hidden: true },
      { key: 'returnCondition', label: 'Condition back', format: 'badge', options: ASSET_CONDITIONS, hidden: true },
      { key: 'assignedBy', label: 'Assigned by', hidden: true }
    ]
  },
  {
    key: 'history', title: 'Asset history', description: 'Complete audit trail of asset events.', icon: 'history',
    endpoint: 'reports/history', defaultSort: 'occurredAt', defaultDir: 'desc',
    columns: [
      { key: 'occurredAt', label: 'When', format: 'datetime' }, { key: 'action', label: 'Action', format: 'badge', options: ASSET_ACTIONS },
      { key: 'assetTag', label: 'Asset', format: 'mono' }, { key: 'assetName', label: 'Asset name' }, { key: 'employeeName', label: 'Employee' },
      { key: 'previousStatus', label: 'From', format: 'badge', options: ASSET_STATUSES }, { key: 'newStatus', label: 'To', format: 'badge', options: ASSET_STATUSES },
      { key: 'performedBy', label: 'By' }, { key: 'notes', label: 'Notes' }
    ]
  },
  {
    key: 'requests', title: 'Requests', description: 'All requests with type, status and requester.', icon: 'inbox',
    endpoint: 'reports/requests', defaultSort: 'createdAt', defaultDir: 'desc',
    columns: [
      { key: 'requestNumber', label: 'Request', format: 'mono' }, { key: 'type', label: 'Type', format: 'badge', options: REQUEST_TYPES },
      { key: 'status', label: 'Status', format: 'badge', options: REQUEST_STATUSES }, { key: 'requesterName', label: 'Requester' },
      { key: 'department', label: 'Department' }, { key: 'assetTag', label: 'Asset', format: 'mono' }, { key: 'comments', label: 'Comments' },
      { key: 'createdAt', label: 'Submitted', format: 'date' }, { key: 'updatedAt', label: 'Updated', format: 'date' }
    ]
  },
  {
    key: 'scrap', title: 'Scrap', description: 'Scrapped assets, reasons and written-off value.', icon: 'trash',
    endpoint: 'reports/scrap', defaultSort: 'scrapDate', defaultDir: 'desc',
    columns: [
      { key: 'scrapDate', label: 'Date', format: 'date' }, { key: 'assetTag', label: 'Asset', format: 'mono' }, { key: 'assetName', label: 'Asset name' },
      { key: 'assetType', label: 'Type' }, { key: 'reason', label: 'Reason' }, { key: 'quantity', label: 'Qty', format: 'number', align: 'end' },
      { key: 'purchaseCost', label: 'Original cost', format: 'money', align: 'end' }, { key: 'performedBy', label: 'By' }, { key: 'notes', label: 'Notes', hidden: true }
    ]
  },
  {
    key: 'repairs', title: 'Repairs', description: 'Repair requests, repairs and component replacements.', icon: 'wrench',
    endpoint: 'reports/repairs', defaultSort: 'occurredAt', defaultDir: 'desc',
    columns: [
      { key: 'occurredAt', label: 'When', format: 'datetime' },
      { key: 'action', label: 'Event', format: 'badge', options: ASSET_ACTIONS.filter(a => ['RepairRequested', 'RepairStarted', 'RepairCompleted', 'ComponentReplaced'].includes(a.value)) },
      { key: 'assetTag', label: 'Asset', format: 'mono' }, { key: 'assetName', label: 'Asset name' }, { key: 'employeeName', label: 'Holder' },
      { key: 'performedBy', label: 'By' }, { key: 'notes', label: 'Details' }
    ]
  }
];

@Component({
  selector: 'app-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DataTableComponent, CellDirective, IconComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header title="Reports" subtitle="Filter any report and export it to CSV (opens in Excel)." icon="chart" />

      <div class="report-grid">
        @for (r of reports; track r.key) {
          <button type="button" class="report" [class.active]="active().key === r.key" (click)="select(r)">
            <span class="r-icon"><app-icon [name]="r.icon" [size]="18" /></span>
            <span class="r-text"><strong>{{ r.title }}</strong><span>{{ r.description }}</span></span>
          </button>
        }
      </div>

      @for (r of [active()]; track r.key) {
        <app-data-table [columns]="r.columns" [fetch]="fetchFor(r)" [storageKey]="'report-' + r.key" [exportName]="'report-' + r.key"
          [defaultSort]="r.defaultSort" [defaultDir]="r.defaultDir" [searchPlaceholder]="'Search ' + r.title.toLowerCase() + '…'"
          emptyTitle="No data for this report yet" [emptyIcon]="r.icon">
          <ng-template appCell="assetTag" let-row>
            @if (row.assetTag) { <a class="tag" [routerLink]="['/assets', row.assetId ?? row.id]">{{ row.assetTag }}</a> } @else { <span class="muted">—</span> }
          </ng-template>
        </app-data-table>
      }
    </div>`,
  styles: `
    .report-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px; }
    .report { display: flex; gap: 12px; align-items: flex-start; text-align: left; padding: 14px; border-radius: 14px; border: 1px solid var(--line);
      background: var(--surface); cursor: pointer; font: inherit; transition: border-color var(--t-fast), box-shadow var(--t-fast), transform var(--t-fast); }
    .report:hover { border-color: var(--line-strong); transform: translateY(-1px); box-shadow: var(--shadow-sm); }
    .report.active { border-color: var(--primary); box-shadow: var(--ring); }
    .r-icon { width: 38px; height: 38px; border-radius: 11px; display: grid; place-items: center; background: var(--primary-soft); color: var(--primary-strong); flex-shrink: 0; }
    .report.active .r-icon { background: var(--grad-indigo); color: #fff; }
    .r-text { display: flex; flex-direction: column; gap: 2px; }
    .r-text strong { font-size: 13.5px; color: var(--ink); }
    .r-text span { font-size: 12px; color: var(--ink-3); line-height: 1.35; }
  `
})
export class ReportsComponent {
  private readonly api = inject(ApiService);
  protected readonly reports = REPORTS;
  protected readonly active = signal<ReportDef>(REPORTS[0]);

  private readonly fetchers = new Map(REPORTS.map(r => [r.key, (q: GridQuery) => this.api.grid<unknown>(r.endpoint, q)]));
  protected readonly fetchFor = (r: ReportDef) => this.fetchers.get(r.key)!;

  protected select(r: ReportDef): void {
    this.active.set(r);
  }

}
