import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, GridQuery } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ASSET_CONDITIONS, TRANSACTION_TYPES } from '../../core/labels';
import { AssetCondition, InventoryItem, InventoryTransaction, StockMovementResult } from '../../core/models';
import { NotifyService, fieldErrorsOf } from '../../core/notify.service';
import { CellDirective, DataTableComponent, RowActionsDirective } from '../../shared/data-table/data-table.component';
import { TableColumn } from '../../shared/data-table/data-table.types';
import { EmployeePickerComponent } from '../../shared/employee-picker.component';
import { IconComponent } from '../../shared/icon.component';
import { ModalComponent } from '../../shared/modal.component';
import { SmartSelectComponent } from '../../shared/smart-select.component';
import { FieldErrorComponent, PageHeaderComponent } from '../../shared/ui';

@Component({
  selector: 'app-inventory',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, DataTableComponent, CellDirective, RowActionsDirective, EmployeePickerComponent, IconComponent,
    ModalComponent, SmartSelectComponent, FieldErrorComponent, PageHeaderComponent],
  templateUrl: './inventory.component.html',
  styleUrl: './inventory.component.css'
})
export class InventoryComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);
  protected readonly auth = inject(AuthService);

  private readonly itemsTable = viewChild<DataTableComponent>('itemsTable');
  private readonly txTable = viewChild<DataTableComponent>('txTable');

  protected readonly tab = signal<'items' | 'transactions'>('items');
  protected readonly stockInOpen = signal(false);
  protected readonly stockOutOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly stockItems = signal<InventoryItem[]>([]);
  protected readonly conditions = ASSET_CONDITIONS;
  protected readonly today = new Date().toISOString().slice(0, 10);

  protected readonly itemColumns: TableColumn[] = [
    { key: 'name', label: 'Item', locked: true },
    { key: 'assetType', label: 'Type' },
    { key: 'brand', label: 'Brand' },
    { key: 'model', label: 'Model', hidden: true },
    { key: 'inStock', label: 'Available', format: 'number', align: 'end' },
    { key: 'assigned', label: 'Assigned', format: 'number', align: 'end' },
    { key: 'underRepair', label: 'Repair', format: 'number', align: 'end' },
    { key: 'damaged', label: 'Damaged', format: 'number', align: 'end', hidden: true },
    { key: 'scrapped', label: 'Scrapped', format: 'number', align: 'end' },
    { key: 'total', label: 'Active total', format: 'number', align: 'end' },
    { key: 'reorderLevel', label: 'Reorder at', format: 'number', align: 'end', hidden: true },
    { key: 'isLowStock', label: 'Low stock', format: 'bool' }
  ];

  protected readonly txColumns: TableColumn[] = [
    { key: 'occurredAt', label: 'Date', format: 'datetime' },
    { key: 'type', label: 'Movement', format: 'badge', options: TRANSACTION_TYPES },
    { key: 'itemName', label: 'Item' },
    { key: 'assetType', label: 'Type', hidden: true },
    { key: 'quantity', label: 'Qty', format: 'number', align: 'end' },
    { key: 'balanceAfter', label: 'Stock after', format: 'number', align: 'end' },
    { key: 'assetTag', label: 'Asset', format: 'mono' },
    { key: 'employeeName', label: 'Employee' },
    { key: 'performedBy', label: 'By' },
    { key: 'notes', label: 'Notes' }
  ];

  protected readonly fetchItems = (q: GridQuery) => this.api.grid<InventoryItem>('inventory', q);
  protected readonly fetchTx = (q: GridQuery) => this.api.grid<InventoryTransaction>('inventory/transactions', q);

  protected readonly stockInForm = this.fb.group({
    assetTypeId: [null as number | null, Validators.required],
    brandId: [null as number | null],
    model: ['', Validators.maxLength(100)],
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    quantity: [1, [Validators.required, Validators.min(1), Validators.max(500)]],
    unitCost: [null as number | null, [Validators.min(0)]],
    purchaseDate: [this.today],
    warrantyExpiry: [''],
    condition: ['New' as AssetCondition],
    locationId: [null as number | null],
    reorderLevel: [null as number | null, [Validators.min(0), Validators.max(100000)]],
    serials: [''],
    notes: ['', Validators.maxLength(2000)]
  });

  protected readonly stockOutForm = this.fb.group({
    inventoryItemId: [null as number | null, Validators.required],
    employeeId: [null as number | null, Validators.required],
    quantity: [1, [Validators.required, Validators.min(1), Validators.max(100)]],
    notes: ['', Validators.maxLength(2000)]
  });

  protected readonly serialCount = signal(0);
  protected readonly selectedStockItem = signal<number | null>(null);
  protected readonly maxOut = computed(() => this.stockItems().find(i => i.id === this.selectedStockItem())?.inStock ?? 0);

  constructor() {
    this.stockInForm.controls.serials.valueChanges.subscribe(v => this.serialCount.set(splitSerials(v ?? '').length));
    this.stockOutForm.controls.inventoryItemId.valueChanges.subscribe(v => this.selectedStockItem.set(v ? Number(v) : null));
  }

  protected openStockIn(): void {
    this.errors.set({});
    this.stockInForm.reset({ quantity: 1, purchaseDate: this.today, condition: 'New', model: '', name: '', serials: '', notes: '', warrantyExpiry: '' });
    this.stockInOpen.set(true);
  }

  protected openStockOut(item?: InventoryItem): void {
    this.errors.set({});
    this.stockOutForm.reset({ inventoryItemId: item?.id ?? null, employeeId: null, quantity: 1, notes: '' });
    this.api.grid<InventoryItem>('inventory', { page: 1, pageSize: 500, sort: 'name', filters: { inStock: '1..' } }).subscribe({
      next: r => this.stockItems.set(r.items),
      error: () => this.stockItems.set([])
    });
    this.stockOutOpen.set(true);
  }

  protected submitStockIn(): void {
    if (this.saving()) return;
    if (this.stockInForm.invalid) return this.stockInForm.markAllAsTouched();
    const v = this.stockInForm.getRawValue();
    const serials = splitSerials(v.serials ?? '');
    if (serials.length > (v.quantity ?? 0)) {
      this.errors.set({ serialNumbers: 'There are more serial numbers than units.' });
      return;
    }
    this.saving.set(true);
    this.errors.set({});
    this.api.post<StockMovementResult>('inventory/stock-in', {
      assetTypeId: v.assetTypeId, brandId: v.brandId, model: v.model?.trim() || null, name: v.name?.trim(), quantity: v.quantity,
      unitCost: v.unitCost, purchaseDate: v.purchaseDate || null, warrantyExpiry: v.warrantyExpiry || null, condition: v.condition,
      locationId: v.locationId, reorderLevel: v.reorderLevel, serialNumbers: serials, notes: v.notes?.trim() || null
    }).subscribe({
      next: r => {
        this.saving.set(false);
        this.stockInOpen.set(false);
        const range = r.assetTags.length > 1 ? `${r.assetTags[0]} – ${r.assetTags[r.assetTags.length - 1]}` : r.assetTags[0];
        this.notify.success(`${r.quantity} unit(s) added to inventory (${range}).`);
        this.reloadAll();
      },
      error: err => this.fail(err)
    });
  }

  protected submitStockOut(): void {
    if (this.saving()) return;
    if (this.stockOutForm.invalid) return this.stockOutForm.markAllAsTouched();
    const v = this.stockOutForm.getRawValue();
    if ((v.quantity ?? 0) > this.maxOut()) {
      this.errors.set({ quantity: `Only ${this.maxOut()} unit(s) available.` });
      return;
    }
    this.saving.set(true);
    this.errors.set({});
    this.api.post<StockMovementResult>('inventory/stock-out', { ...v, notes: v.notes?.trim() || null }).subscribe({
      next: r => {
        this.saving.set(false);
        this.stockOutOpen.set(false);
        this.notify.success(`Issued ${r.quantity} unit(s): ${r.assetTags.join(', ')}.`);
        this.reloadAll();
      },
      error: err => this.fail(err)
    });
  }

  private fail(err: unknown): void {
    this.saving.set(false);
    const fields = fieldErrorsOf(err);
    this.errors.set(fields);
    if (Object.keys(fields).length === 0) this.notify.apiError(err);
  }

  protected async removeItem(item: InventoryItem): Promise<void> {
    if (!(await this.notify.confirm({
      title: `Delete ${item.name}?`,
      text: 'The item and its stock movements will be removed permanently. This cannot be undone.',
      confirmText: 'Delete permanently',
      danger: true
    }))) return;
    this.api.delete(`inventory/${item.id}`).subscribe({
      next: () => {
        this.notify.success(`${item.name} deleted.`);
        this.reloadAll();
      },
      error: err => this.notify.apiError(err, 'Cannot delete')
    });
  }

  private reloadAll(): void {
    this.itemsTable()?.reload();
    this.txTable()?.reload();
  }
}

function splitSerials(text: string): string[] {
  return text.split(/[\n,;]+/).map(s => s.trim()).filter(Boolean);
}
