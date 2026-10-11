import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService, GridQuery } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { REQUEST_STATUSES, REQUEST_TYPES } from '../../core/labels';
import { RequestDetail, RequestListItem } from '../../core/models';
import { CellDirective, DataTableComponent, RowActionsDirective } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { IconComponent } from '../../shared/icon.component';
import { PageHeaderComponent } from '../../shared/ui';
import { RequestFormComponent } from './request-form.component';

@Component({
  selector: 'app-requests-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DataTableComponent, CellDirective, RowActionsDirective, IconComponent, PageHeaderComponent, RequestFormComponent],
  template: `
    <div class="page">
      <app-page-header title="Requests" [subtitle]="seesAll() ? 'Review, process and close employee requests.' : 'Track the requests you have raised.'" icon="inbox">
        @if (canCreate()) {
          <button type="button" class="btn btn-primary" (click)="formOpen.set(true)"><app-icon name="plus" [size]="16" /> New request</button>
        }
      </app-page-header>

      <div class="row wrap">
        @if (seesAll()) {
          <div class="tabs" role="tablist">
            <button type="button" role="tab" [class.active]="!mine()" (click)="setMine(false)">All requests</button>
            <button type="button" role="tab" [class.active]="mine()" (click)="setMine(true)">Raised by me</button>
          </div>
        }
        <div class="chips">
          @for (chip of chips; track chip.label) {
            <button type="button" class="chip" [class.active]="activeChip() === chip.value" (click)="selectChip(chip.value)">{{ chip.label }}</button>
          }
        </div>
      </div>

      @if (tableVisible()) {
        <app-data-table #table [columns]="columns" [fetch]="fetch" storageKey="requests" exportName="requests" defaultSort="createdAt" defaultDir="desc"
          searchPlaceholder="Search number, requester, comments…" emptyTitle="No requests found"
          [emptyMessage]="canCreate() ? 'Raise a request when you need a repair, replacement or new equipment.' : 'Requests will appear here.'" emptyIcon="inbox">
          <ng-template appCell="requestNumber" let-row><a class="tag" [routerLink]="['/requests', row.id]">{{ row.requestNumber }}</a></ng-template>
          <ng-template appCell="assetTag" let-row>
            @if (row.assetTag) { <span class="tag">{{ row.assetTag }}</span> <span class="muted small">{{ row.assetName }}</span> } @else { <span class="muted">—</span> }
          </ng-template>
          <ng-template appRowActions let-row>
            <a class="btn btn-sm" [class.btn-soft]="needsMe(row)" [routerLink]="['/requests', row.id]">
              @if (needsMe(row)) { <app-icon name="flag" [size]="14" /> Action } @else { Open }
            </a>
          </ng-template>
        </app-data-table>
      }
    </div>
    <app-request-form [open]="formOpen()" (closed)="formOpen.set(false)" (created)="onCreated($event)" />
  `,
  styles: `
    .chips { display: flex; gap: 6px; flex-wrap: wrap; }
    .chip { height: 32px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--line); background: var(--surface); color: var(--ink-2);
      font: 600 12.5px/1 var(--font-body); cursor: pointer; transition: all var(--t-fast); }
    .chip:hover { border-color: var(--line-strong); }
    .chip.active { background: var(--ink); border-color: var(--ink); color: #fff; }
  `
})
export class RequestsListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  private readonly table = viewChild<DataTableComponent>('table');

  readonly new = input<string>();

  protected readonly formOpen = signal(false);
  protected readonly mine = signal(false);
  protected readonly tableVisible = signal(true);
  protected readonly activeChip = signal('');
  protected readonly seesAll = computed(() => this.auth.canAny('Requests.View', 'Requests.Approve', 'Requests.Process', 'Requests.Complete'));
  protected readonly canCreate = computed(() => this.auth.can('Requests.Create') && !!this.auth.user()?.employeeId);

  protected readonly chips = [
    { value: '', label: 'Everything' },
    { value: 'Submitted', label: 'Needs approval' },
    { value: 'Approved,InProgress', label: 'In progress' },
    { value: 'Delivered,Checking', label: 'With employee' },
    { value: 'Received', label: 'Ready to close' },
    { value: 'Done', label: 'Done' },
    { value: 'Rejected', label: 'Rejected' }
  ];

  protected readonly columns: TableColumn[] = [
    { key: 'requestNumber', label: 'Request', format: 'mono', locked: true },
    { key: 'type', label: 'Type', format: 'badge', options: REQUEST_TYPES },
    { key: 'status', label: 'Status', format: 'badge', options: REQUEST_STATUSES },
    { key: 'requesterName', label: 'Requester' },
    { key: 'department', label: 'Department', hidden: true },
    { key: 'assetTag', label: 'Asset' },
    { key: 'comments', label: 'Comments' },
    { key: 'createdAt', label: 'Submitted', format: 'datetime' },
    { key: 'updatedAt', label: 'Last update', format: 'datetime', hidden: true }
  ];

  protected readonly fetch = (q: GridQuery) => this.api.grid<RequestListItem>('requests', q, this.mine() ? { mine: true } : undefined);

  ngOnInit(): void {
    if (this.new() && this.canCreate()) this.formOpen.set(true);
  }

  /** Highlight rows where the signed-in user owns the next step. */
  protected needsMe(r: RequestListItem): boolean {
    const isRequester = r.requesterEmployeeId === this.auth.user()?.employeeId;
    switch (r.status) {
      case 'Submitted': return this.auth.can('Requests.Approve');
      case 'Approved': case 'InProgress': return this.auth.can('Requests.Process');
      case 'Delivered': case 'Checking': return isRequester;
      case 'Received': return this.auth.can('Requests.Complete');
      default: return false;
    }
  }

  protected setMine(value: boolean): void {
    this.mine.set(value);
    this.table()?.reload();
  }

  protected selectChip(value: string): void {
    this.activeChip.set(value);
    this.table()?.setFilter('status', value);
  }

  protected onCreated(detail: RequestDetail): void {
    this.formOpen.set(false);
    this.router.navigate(['/requests', detail.request.id]);
  }
}
