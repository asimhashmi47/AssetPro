import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ASSET_CONDITIONS } from '../../core/labels';
import { AssetCondition, AssetDetail, AssetListItem } from '../../core/models';
import { NotifyService, fieldErrorsOf } from '../../core/notify.service';
import { clearServerErrorsOnChange } from '../../core/forms';
import { ModalComponent } from '../../shared/modal.component';
import { SmartSelectComponent } from '../../shared/smart-select.component';
import { FieldErrorComponent } from '../../shared/ui';

const notFuture = (c: AbstractControl): ValidationErrors | null =>
  c.value && c.value > new Date().toISOString().slice(0, 10) ? { futureDate: true } : null;

const warrantyAfterPurchase = (g: AbstractControl): ValidationErrors | null => {
  const p = g.get('purchaseDate')?.value;
  const w = g.get('warrantyExpiry')?.value;
  return p && w && w < p ? { warrantyBeforePurchase: true } : null;
};

/** Create / edit asset dialog. */
@Component({
  selector: 'app-asset-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ModalComponent, SmartSelectComponent, FieldErrorComponent],
  template: `
    <app-modal [open]="open()" [title]="detail() ? 'Edit asset' : 'Add asset'" [subtitle]="detail() ? detail()!.asset.assetTag : 'Register a single tracked item'"
      [icon]="detail() ? 'edit' : 'plus'" size="lg" [busy]="saving()" (closed)="closed.emit()">
      <form id="asset-form" class="form-grid" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <div class="field full">
          <label for="a-name">Asset name<span class="req">*</span></label>
          <input id="a-name" class="input" formControlName="name" maxlength="150" placeholder="e.g. Dell Latitude 5440" />
          <app-field-error [control]="form.controls.name" name="Asset name" [server]="errors()['name']" />
        </div>
        <div class="field">
          <label for="a-type">Asset type<span class="req">*</span></label>
          <app-smart-select inputId="a-type" kind="asset-types" formControlName="assetTypeId" placeholder="Select type" />
          <app-field-error [control]="form.controls.assetTypeId" name="Asset type" [server]="errors()['assetTypeId']" />
        </div>
        <div class="field">
          <label for="a-tag">Asset tag</label>
          <input id="a-tag" class="input mono-input" formControlName="assetTag" maxlength="30" placeholder="Auto-generated (e.g. LAP-00012)" />
          <app-field-error [control]="form.controls.assetTag" name="Asset tag" [server]="errors()['assetTag']" />
        </div>
        <div class="field">
          <label for="a-brand">Brand</label>
          <app-smart-select inputId="a-brand" kind="brands" formControlName="brandId" placeholder="Select brand" />
        </div>
        <div class="field">
          <label for="a-model">Model</label>
          <input id="a-model" class="input" formControlName="model" maxlength="100" placeholder="e.g. Latitude 5440" />
        </div>
        <div class="field">
          <label for="a-serial">Serial number</label>
          <input id="a-serial" class="input" formControlName="serialNumber" maxlength="100" placeholder="Must be unique" />
          <app-field-error [control]="form.controls.serialNumber" name="Serial number" [server]="errors()['serialNumber']" />
        </div>
        <div class="field">
          <label for="a-condition">Condition</label>
          <select id="a-condition" class="select" formControlName="condition">
            @for (c of conditions; track c.value) { <option [value]="c.value">{{ c.label }}</option> }
          </select>
        </div>
        <div class="field">
          <label for="a-pdate">Purchase date</label>
          <input id="a-pdate" class="input" type="date" formControlName="purchaseDate" [max]="today" />
          <app-field-error [control]="form.controls.purchaseDate" name="Purchase date" [server]="errors()['purchaseDate']" />
        </div>
        <div class="field">
          <label for="a-cost">Purchase cost</label>
          <input id="a-cost" class="input" type="number" min="0" step="0.01" inputmode="decimal" formControlName="purchaseCost" placeholder="0.00" />
          <app-field-error [control]="form.controls.purchaseCost" name="Purchase cost" [server]="errors()['purchaseCost']" />
        </div>
        <div class="field">
          <label for="a-warranty">Warranty expiry</label>
          <input id="a-warranty" class="input" type="date" formControlName="warrantyExpiry" />
          @if (form.hasError('warrantyBeforePurchase')) { <span class="error">Warranty expiry cannot be before the purchase date.</span> }
          <app-field-error [server]="errors()['warrantyExpiry']" />
        </div>
        <div class="field">
          <label for="a-location">Location</label>
          <app-smart-select inputId="a-location" kind="locations" formControlName="locationId" placeholder="Select location" />
        </div>
        <div class="field full">
          <label for="a-notes">Notes</label>
          <textarea id="a-notes" class="textarea" formControlName="notes" maxlength="2000" rows="3" placeholder="Anything worth knowing about this asset"></textarea>
        </div>
      </form>
      <ng-container modal-footer>
        <button type="button" class="btn" (click)="closed.emit()" [disabled]="saving()">Cancel</button>
        <button type="submit" form="asset-form" class="btn btn-primary" [disabled]="saving()">
          @if (saving()) { <span class="spinner"></span> } {{ detail() ? 'Save changes' : 'Create asset' }}
        </button>
      </ng-container>
    </app-modal>`,
  styles: `.mono-input { font-family: var(--font-mono); font-size: 13px; text-transform: uppercase; }`
})
export class AssetFormComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);

  readonly open = input(false);
  readonly detail = input<AssetDetail | null>(null);
  readonly closed = output<void>();
  readonly saved = output<AssetListItem>();

  protected readonly conditions = ASSET_CONDITIONS;
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string>>({});

  protected readonly form = inject(FormBuilder).group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    assetTypeId: [null as number | null, Validators.required],
    assetTag: ['', [Validators.maxLength(30), Validators.pattern(/^[A-Za-z0-9\-_/]*$/)]],
    brandId: [null as number | null],
    model: ['', Validators.maxLength(100)],
    serialNumber: ['', Validators.maxLength(100)],
    condition: ['New' as AssetCondition],
    purchaseDate: ['', notFuture],
    purchaseCost: [null as number | null, [Validators.min(0), Validators.max(100000000)]],
    warrantyExpiry: [''],
    locationId: [null as number | null],
    notes: ['', Validators.maxLength(2000)]
  }, { validators: warrantyAfterPurchase });

  constructor() {
    clearServerErrorsOnChange(this.form, this.errors, inject(DestroyRef));
    effect(() => {
      if (!this.open()) return;
      this.errors.set({});
      const d = this.detail();
      if (d) {
        const a = d.asset;
        this.form.reset({
          name: a.name, assetTypeId: a.assetTypeId, assetTag: a.assetTag, brandId: a.brandId, model: a.model ?? '',
          serialNumber: a.serialNumber ?? '', condition: a.condition, purchaseDate: a.purchaseDate ?? '', purchaseCost: a.purchaseCost,
          warrantyExpiry: a.warrantyExpiry ?? '', locationId: a.locationId, notes: d.notes ?? ''
        });
      } else {
        this.form.reset({ condition: 'New', name: '', assetTag: '', model: '', serialNumber: '', purchaseDate: '', warrantyExpiry: '', notes: '' });
      }
    });
  }

  protected save(): void {
    if (this.saving()) return;
    if (this.form.invalid) return this.form.markAllAsTouched();
    this.saving.set(true);
    this.errors.set({});
    const v = this.form.getRawValue();
    const body = {
      ...v,
      assetTag: v.assetTag?.trim() || null,
      model: v.model?.trim() || null,
      serialNumber: v.serialNumber?.trim() || null,
      purchaseDate: v.purchaseDate || null,
      warrantyExpiry: v.warrantyExpiry || null,
      notes: v.notes?.trim() || null
    };
    const d = this.detail();
    const request = d ? this.api.put<AssetListItem>(`assets/${d.asset.id}`, body) : this.api.post<AssetListItem>('assets', body);
    request.subscribe({
      next: asset => {
        this.saving.set(false);
        this.notify.success(d ? 'Asset updated successfully.' : `Asset ${asset.assetTag} created successfully.`);
        this.saved.emit(asset);
      },
      error: err => {
        this.saving.set(false);
        const fields = fieldErrorsOf(err);
        this.errors.set(fields);
        if (Object.keys(fields).length === 0) this.notify.apiError(err);
      }
    });
  }
}
