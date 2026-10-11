import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService, GridQuery } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ASSET_CONDITIONS, ASSET_STATUSES } from '../../core/labels';
import { AssetListItem } from '../../core/models';
import { CellDirective, DataTableComponent, RowActionsDirective } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { IconComponent } from '../../shared/icon.component';
import { PageHeaderComponent } from '../../shared/ui';
import { AssetActionMode, AssetActionsComponent } from './asset-actions.component';
import { AssetFormComponent } from './asset-form.component';

@Component({
  selector: 'app-assets-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DataTableComponent, CellDirective, RowActionsDirective, IconComponent, PageHeaderComponent, AssetFormComponent, AssetActionsComponent],
  template: `
    <div class="page">
      <app-page-header title="Assets" subtitle="Every tracked item, where it is and who holds it." icon="box">
        @if (auth.can('Assets.Create')) {
          <button type="button" class="btn btn-primary" (click)="formOpen.set(true)"><app-icon name="plus" [size]="16" /> Add asset</button>
        }
      </app-page-header>

      <div class="chips" role="tablist" aria-label="Filter by status">
        @for (chip of chips; track chip.value) {
          <button type="button" role="tab" class="chip" [class.active]="activeChip() === chip.value" [attr.aria-selected]="activeChip() === chip.value" (click)="selectChip(chip.value)">
            <span class="dot" [style.background]="chip.color"></span>{{ chip.label }}
          </button>
        }
      </div>

      <app-data-table #table [columns]="columns" [fetch]="fetch" storageKey="assets" exportName="assets" defaultSort="createdAt" defaultDir="desc"
        searchPlaceholder="Search tag, name, serial, holder…" emptyTitle="No assets found" emptyMessage="Add your first asset or stock in a batch from Inventory." emptyIcon="box">
        <ng-template appCell="assetTag" let-row><a class="tag" [routerLink]="['/assets', row.id]">{{ row.assetTag }}</a></ng-template>
        <ng-template appCell="name" let-row>
          <a class="name-link" [routerLink]="['/assets', row.id]">{{ row.name }}</a>
          @if (row.model) { <span class="sub">{{ row.brand ? row.brand + ' · ' : '' }}{{ row.model }}</span> }
        </ng-template>
        <ng-template appCell="assignedTo" let-row>
          @if (row.assignedTo) {
            @if (auth.can('Employees.View')) { <a [routerLink]="['/employees', row.assignedToEmployeeId]">{{ row.assignedTo }}</a> } @else { {{ row.assignedTo }} }
          } @else { <span class="muted">—</span> }
        </ng-template>
        <ng-template appRowActions let-row>
          @if (row.status === 'InStock' && auth.can('Assets.Assign')) {
            <button type="button" class="btn btn-sm btn-soft" (click)="act(row, 'assign')" title="Assign"><app-icon name="user-check" [size]="15" /> Assign</button>
          }
          @if (row.status === 'Assigned' && auth.can('Assets.Return')) {
            <button type="button" class="btn btn-sm" (click)="act(row, 'return')" title="Return"><app-icon name="undo" [size]="15" /> Return</button>
          }
          <a class="btn btn-sm btn-ghost btn-icon" [routerLink]="['/assets', row.id]" title="View details" aria-label="View details"><app-icon name="eye" [size]="16" /></a>
        </ng-template>
      </app-data-table>
    </div>

    <app-asset-form [open]="formOpen()" (closed)="formOpen.set(false)" (saved)="onCreated($event)" />
    <app-asset-actions #actions [asset]="actionAsset()" (done)="table.reload()" />
  `,
  styles: `
    .chips { display: flex; gap: 8px; flex-wrap: wrap; }
    .chip { display: inline-flex; align-items: center; gap: 8px; height: 34px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--line);
      background: var(--surface); color: var(--ink-2); font: 600 13px/1 var(--font-body); cursor: pointer; transition: all var(--t-fast); }
    .chip:hover { border-color: var(--line-strong); }
    .chip.active { background: var(--ink); border-color: var(--ink); color: #fff; }
    .chip .dot { width: 8px; height: 8px; border-radius: 50%; }
    .name-link { color: var(--ink); font-weight: 600; display: block; max-width: 230px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .sub { display: block; font-size: 12px; color: var(--ink-3); max-width: 230px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `
})
export class AssetsListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  /** ?new=1 opens the create dialog (used by dashboard shortcut). */
  readonly new = input<string>();

  private readonly table = viewChild.required<DataTableComponent>('table');
  private readonly actions = viewChild.required<AssetActionsComponent>('actions');

  protected readonly formOpen = signal(false);
  protected readonly actionAsset = signal<AssetListItem | null>(null);
  protected readonly activeChip = signal('');

  protected readonly chips = [
    { value: '', label: 'All', color: 'var(--ink-4)' },
    { value: 'InStock', label: 'In stock', color: 'var(--success)' },
    { value: 'Assigned', label: 'Assigned', color: 'var(--primary)' },
    { value: 'UnderRepair', label: 'Under repair', color: 'var(--warning)' },
    { value: 'Damaged', label: 'Damaged', color: 'var(--danger)' },
    { value: 'Scrapped', label: 'Scrapped', color: '#94a3b8' },
    { value: 'Retired', label: 'Retired', color: '#cbd5e1' }
  ];

  protected readonly columns: TableColumn[] = [
    { key: 'assetTag', label: 'Asset tag', format: 'mono', locked: true },
    { key: 'name', label: 'Asset', locked: true },
    { key: 'assetType', label: 'Type' },
    { key: 'brand', label: 'Brand', hidden: true },
    { key: 'model', label: 'Model', hidden: true },
    { key: 'serialNumber', label: 'Serial no.', format: 'mono' },
    { key: 'status', label: 'Status', format: 'badge', options: ASSET_STATUSES },
    { key: 'condition', label: 'Condition', format: 'badge', options: ASSET_CONDITIONS },
    { key: 'assignedTo', label: 'Assigned to' },
    { key: 'location', label: 'Location', hidden: true },
    { key: 'purchaseDate', label: 'Purchased', format: 'date', hidden: true },
    { key: 'purchaseCost', label: 'Cost', format: 'money', align: 'end', hidden: true },
    { key: 'warrantyExpiry', label: 'Warranty until', format: 'date' },
    { key: 'createdAt', label: 'Added', format: 'datetime', hidden: true }
  ];

  protected readonly fetch = (q: GridQuery) => this.api.grid<AssetListItem>('assets', q);

  ngOnInit(): void {
    if (this.new() && this.auth.can('Assets.Create')) this.formOpen.set(true);
  }

  protected selectChip(value: string): void {
    this.activeChip.set(value);
    this.table().setFilter('status', value);
  }

  protected act(row: AssetListItem, mode: AssetActionMode): void {
    this.actionAsset.set(row);
    this.actions().start(mode);
  }

  protected onCreated(asset: AssetListItem): void {
    this.formOpen.set(false);
    this.router.navigate(['/assets', asset.id]);
  }
}
