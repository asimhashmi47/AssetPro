import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ASSET_CONDITIONS } from '../../core/labels';
import { AssetListItem, EmployeeOption } from '../../core/models';
import { NotifyService, fieldErrorsOf, messageOf } from '../../core/notify.service';
import { EmployeePickerComponent } from '../../shared/employee-picker.component';
import { ModalComponent } from '../../shared/modal.component';
import { FieldErrorComponent } from '../../shared/ui';

export type AssetActionMode = 'assign' | 'return' | 'repair-start' | 'repair-complete' | 'replace' | 'scrap' | 'damaged' | 'retire' | 'release';

interface ModeMeta {
  title: string;
  icon: string;
  tone: 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'violet';
  submit: string;
}

const META: Record<AssetActionMode, ModeMeta> = {
  assign: { title: 'Assign asset', icon: 'user-check', tone: 'primary', submit: 'Assign asset' },
  return: { title: 'Process return', icon: 'undo', tone: 'info', submit: 'Confirm return' },
  'repair-start': { title: 'Send to repair', icon: 'wrench', tone: 'warning', submit: 'Start repair' },
  'repair-complete': { title: 'Complete repair', icon: 'check-circle', tone: 'success', submit: 'Mark repaired' },
  replace: { title: 'Replace component', icon: 'cpu', tone: 'violet', submit: 'Record replacement' },
  scrap: { title: 'Move to scrap', icon: 'trash', tone: 'danger', submit: 'Scrap asset' },
  damaged: { title: 'Mark as damaged', icon: 'alert', tone: 'danger', submit: 'Mark damaged' },
  retire: { title: 'Retire asset', icon: 'archive', tone: 'warning', submit: 'Retire asset' },
  release: { title: 'Release to stock', icon: 'package-plus', tone: 'success', submit: 'Release to stock' }
};

/**
 * One dialog for every lifecycle action. The API re-validates every rule; this form only
 * collects the inputs and asks for confirmation on the irreversible ones.
 */
@Component({
  selector: 'app-asset-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ModalComponent, FieldErrorComponent, EmployeePickerComponent],
  templateUrl: './asset-actions.component.html',
  styles: `
    .asset-chip { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: 12px; background: var(--surface-2); border: 1px solid var(--line); margin-bottom: 16px; }
    .asset-chip strong { font-size: 13.5px; }
    .asset-chip span { font-size: 12.5px; color: var(--ink-3); }
    .warn { display: flex; gap: 8px; padding: 10px 12px; border-radius: 10px; background: var(--danger-soft); color: var(--danger-ink); font-size: 13px; margin-bottom: 14px; }
  `
})
export class AssetActionsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);

  readonly asset = input<AssetListItem | null>(null);
  readonly done = output<void>();

  protected readonly mode = signal<AssetActionMode | null>(null);
  protected readonly meta = computed(() => (this.mode() ? META[this.mode()!] : null));
  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly conditions = ASSET_CONDITIONS;
  protected readonly nowLocal = toLocalInput(new Date());
  protected readonly today = new Date().toISOString().slice(0, 10);

  /** Kept so confirmations can name the person; the picker owns the search itself. */
  protected readonly selectedEmployee = signal<EmployeeOption | null>(null);

  protected readonly assignForm = this.fb.group({
    employeeId: [null as number | null, Validators.required],
    assignedAt: [this.nowLocal],
    condition: [''],
    notes: ['', Validators.maxLength(2000)]
  });

  protected readonly returnForm = this.fb.nonNullable.group({
    returnedAt: [this.nowLocal],
    returnedBy: ['', Validators.maxLength(150)],
    condition: ['Good'],
    resultingStatus: ['InStock'],
    damageDetails: ['', Validators.maxLength(2000)],
    scrapReason: ['', Validators.maxLength(500)],
    notes: ['', Validators.maxLength(2000)]
  });

  protected readonly repairForm = this.fb.nonNullable.group({
    notes: ['', Validators.maxLength(2000)],
    resultingCondition: ['Good']
  });

  protected readonly replaceForm = this.fb.nonNullable.group({
    component: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    oldComponent: ['', Validators.maxLength(150)],
    newComponent: ['', [Validators.required, Validators.maxLength(150)]],
    reason: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(500)]],
    replacedOn: [this.today],
    notes: ['', Validators.maxLength(2000)]
  });

  protected readonly scrapForm = this.fb.nonNullable.group({
    reason: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(500)]],
    scrapDate: [this.today],
    notes: ['', Validators.maxLength(2000)]
  });

  protected readonly statusForm = this.fb.nonNullable.group({ notes: ['', Validators.maxLength(2000)] });

  protected readonly commonComponents = ['RAM', 'SSD / Storage', 'Battery', 'Keyboard', 'Screen', 'Charger', 'Motherboard', 'Fan'];

  /** Opens the dialog in the given mode, resetting its form. */
  start(mode: AssetActionMode): void {
    this.errors.set({});
    const now = toLocalInput(new Date());
    switch (mode) {
      case 'assign':
        this.assignForm.reset({ employeeId: null, assignedAt: now, condition: '', notes: '' });
        this.selectedEmployee.set(null);
        break;
      case 'return':
        this.returnForm.reset({ returnedAt: now, returnedBy: '', condition: 'Good', resultingStatus: 'InStock', damageDetails: '', scrapReason: '', notes: '' });
        break;
      case 'repair-start':
      case 'repair-complete':
        this.repairForm.reset({ notes: '', resultingCondition: 'Good' });
        break;
      case 'replace':
        this.replaceForm.reset({ component: '', oldComponent: '', newComponent: '', reason: '', replacedOn: new Date().toISOString().slice(0, 10), notes: '' });
        break;
      case 'scrap':
        this.scrapForm.reset({ reason: '', scrapDate: new Date().toISOString().slice(0, 10), notes: '' });
        break;
      default:
        this.statusForm.reset({ notes: '' });
    }
    this.mode.set(mode);
  }

  protected close(): void {
    if (!this.saving()) this.mode.set(null);
  }

  protected async submit(): Promise<void> {
    const mode = this.mode();
    const asset = this.asset();
    if (!mode || !asset || this.saving()) return;

    let path = '';
    let body: unknown = {};
    let success = '';

    switch (mode) {
      case 'assign': {
        if (this.assignForm.invalid) return this.assignForm.markAllAsTouched();
        const v = this.assignForm.getRawValue();
        const who = this.selectedEmployee()?.fullName ?? 'the employee';
        if (!(await this.notify.confirm({ title: `Assign ${asset.assetTag}?`, text: `${asset.name} will be assigned to ${who}.`, confirmText: 'Assign' }))) return;
        path = 'assign';
        body = { employeeId: v.employeeId, assignedAt: fromLocalInput(v.assignedAt), condition: v.condition || null, notes: v.notes || null };
        success = `${asset.name} assigned to ${who} successfully.`;
        break;
      }
      case 'return': {
        if (this.returnForm.invalid) return this.returnForm.markAllAsTouched();
        const v = this.returnForm.getRawValue();
        if (v.resultingStatus === 'Scrapped') {
          if (!v.scrapReason.trim()) { this.errors.set({ scrapReason: 'A scrap reason is required.' }); return; }
          if (!(await this.notify.confirm({ title: 'Return and scrap?', text: `${asset.assetTag} will be returned and permanently moved to scrap.`, confirmText: 'Return & scrap', danger: true }))) return;
        }
        path = 'return';
        body = { ...v, returnedAt: fromLocalInput(v.returnedAt), returnedBy: v.returnedBy || null, damageDetails: v.damageDetails || null,
          scrapReason: v.scrapReason || null, notes: v.notes || null };
        success = 'Asset returned successfully.';
        break;
      }
      case 'repair-start':
      case 'repair-complete': {
        const v = this.repairForm.getRawValue();
        path = 'repair';
        body = { action: mode === 'repair-start' ? 'Start' : 'Complete', notes: v.notes || null,
          resultingCondition: mode === 'repair-complete' ? v.resultingCondition : null };
        success = mode === 'repair-start' ? 'Asset sent to repair.' : 'Repair completed.';
        break;
      }
      case 'replace': {
        if (this.replaceForm.invalid) return this.replaceForm.markAllAsTouched();
        const v = this.replaceForm.getRawValue();
        path = 'replace-component';
        body = { ...v, oldComponent: v.oldComponent || null, replacedOn: v.replacedOn || null, notes: v.notes || null };
        success = `${v.component} replacement recorded.`;
        break;
      }
      case 'scrap': {
        if (this.scrapForm.invalid) return this.scrapForm.markAllAsTouched();
        if (!(await this.notify.confirm({ title: `Scrap ${asset.assetTag}?`, text: 'Scrapped assets can never be assigned again. The record and its history are kept.', confirmText: 'Move to scrap', danger: true }))) return;
        const v = this.scrapForm.getRawValue();
        path = 'scrap';
        body = { ...v, scrapDate: v.scrapDate || null, notes: v.notes || null };
        success = 'Asset moved to scrap.';
        break;
      }
      case 'damaged':
      case 'retire':
      case 'release': {
        if (mode === 'retire' && !(await this.notify.confirm({ title: `Retire ${asset.assetTag}?`, text: 'Retired assets cannot be assigned again.', confirmText: 'Retire', danger: true }))) return;
        path = 'status';
        body = { status: mode === 'damaged' ? 'Damaged' : mode === 'retire' ? 'Retired' : 'InStock', notes: this.statusForm.getRawValue().notes || null };
        success = mode === 'damaged' ? 'Asset marked as damaged.' : mode === 'retire' ? 'Asset retired.' : 'Asset released to stock.';
        break;
      }
    }

    this.saving.set(true);
    this.errors.set({});
    this.api.post(`assets/${asset.id}/${path}`, body).subscribe({
      next: () => {
        this.saving.set(false);
        this.mode.set(null);
        this.notify.success(success);
        this.done.emit();
      },
      error: err => {
        this.saving.set(false);
        const fields = fieldErrorsOf(err);
        this.errors.set(fields);
        if (Object.keys(fields).length === 0) this.notify.error(messageOf(err), 'Action not allowed');
      }
    });
  }
}

function toLocalInput(date: Date): string {
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** datetime-local value (viewer's local time) → UTC ISO string for the API. */
function fromLocalInput(value: string | null | undefined): string | null {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}
