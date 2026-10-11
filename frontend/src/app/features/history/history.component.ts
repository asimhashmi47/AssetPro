import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, GridQuery } from '../../core/api.service';
import { ASSET_ACTIONS, ASSET_STATUSES } from '../../core/labels';
import { AssetHistoryItem } from '../../core/models';
import { CellDirective, DataTableComponent } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { IconComponent } from '../../shared/icon.component';
import { HistoryTimelineComponent } from '../../shared/timeline.component';
import { ErrorStateComponent, PageHeaderComponent } from '../../shared/ui';

/** Company-wide asset history: a filterable table, or a timeline view of the latest events. */
@Component({
  selector: 'app-history',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DataTableComponent, CellDirective, IconComponent, HistoryTimelineComponent, ErrorStateComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header title="Asset History" subtitle="Every assignment, return, repair, replacement and status change — kept forever." icon="history" />

      <div class="tabs" role="tablist">
        <button type="button" role="tab" [class.active]="view() === 'table'" (click)="view.set('table')"><app-icon name="columns" [size]="15" /> Table</button>
        <button type="button" role="tab" [class.active]="view() === 'timeline'" (click)="showTimeline()"><app-icon name="activity" [size]="15" /> Timeline</button>
      </div>

      @if (view() === 'table') {
        <app-data-table [columns]="columns" [fetch]="fetch" storageKey="history" exportName="asset-history" defaultSort="occurredAt" defaultDir="desc"
          searchPlaceholder="Search asset, employee, notes…" emptyTitle="No asset history yet" emptyIcon="history">
          <ng-template appCell="assetTag" let-row><a class="tag" [routerLink]="['/assets', row.assetId]">{{ row.assetTag }}</a></ng-template>
          <ng-template appCell="employeeName" let-row>
            @if (row.employeeId) { <a [routerLink]="['/employees', row.employeeId]">{{ row.employeeName }}</a> } @else { <span class="muted">—</span> }
          </ng-template>
        </app-data-table>
      } @else {
        <article class="card">
          <div class="card-head"><div><h2>Latest 50 events</h2><span class="sub">Newest first</span></div></div>
          <div class="card-body">
            @if (timelineError()) { <app-error-state (retry)="showTimeline()" /> }
            @else if (timeline(); as items) { <app-history-timeline [items]="items" /> }
            @else { @for (i of [1, 2, 3, 4, 5]; track i) { <div class="skeleton" style="height: 48px; margin-bottom: 12px"></div> } }
          </div>
        </article>
      }
    </div>`
})
export class HistoryComponent {
  private readonly api = inject(ApiService);

  protected readonly view = signal<'table' | 'timeline'>('table');
  protected readonly timeline = signal<AssetHistoryItem[] | null>(null);
  protected readonly timelineError = signal(false);

  protected readonly columns: TableColumn[] = [
    { key: 'occurredAt', label: 'When', format: 'datetime' },
    { key: 'action', label: 'Action', format: 'badge', options: ASSET_ACTIONS },
    { key: 'assetTag', label: 'Asset', format: 'mono' },
    { key: 'assetName', label: 'Asset name' },
    { key: 'assetType', label: 'Type', hidden: true },
    { key: 'employeeName', label: 'Employee' },
    { key: 'previousStatus', label: 'From', format: 'badge', options: ASSET_STATUSES },
    { key: 'newStatus', label: 'To', format: 'badge', options: ASSET_STATUSES },
    { key: 'performedBy', label: 'Performed by' },
    { key: 'notes', label: 'Notes' }
  ];

  protected readonly fetch = (q: GridQuery) => this.api.grid<AssetHistoryItem>('history', q);

  protected showTimeline(): void {
    this.view.set('timeline');
    this.timelineError.set(false);
    this.api.grid<AssetHistoryItem>('history', { page: 1, pageSize: 50, sort: 'occurredAt', dir: 'desc' }).subscribe({
      next: r => this.timeline.set(r.items),
      error: () => this.timelineError.set(true)
    });
  }
}
