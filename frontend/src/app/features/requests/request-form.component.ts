import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { REQUEST_TYPES } from '../../core/labels';
import { AssetListItem, EmployeeProfile, RequestDetail, RequestType } from '../../core/models';
import { NotifyService, fieldErrorsOf } from '../../core/notify.service';
import { clearServerErrorsOnChange } from '../../core/forms';
import { IconComponent } from '../../shared/icon.component';
import { ModalComponent } from '../../shared/modal.component';
import { FieldErrorComponent } from '../../shared/ui';

const TYPE_HELP: Record<RequestType, { icon: string; text: string }> = {
  Repair: { icon: 'wrench', text: 'Something is broken or misbehaving.' },
  Replacement: { icon: 'refresh', text: 'Swap a worn-out item for a new one.' },
  NewAsset: { icon: 'package-plus', text: 'You need equipment you don’t have yet.' },
  Return: { icon: 'undo', text: 'Hand an item back to IT.' },
  Other: { icon: 'message', text: 'Anything else IT can help with.' }
};

/** Minimal request form: type, asset, comments. */
@Component({
  selector: 'app-request-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ModalComponent, IconComponent, FieldErrorComponent],
  template: `
    <app-modal [open]="open()" title="New request" subtitle="Tell IT what you need. You can follow every step afterwards." icon="send" tone="violet"
      [busy]="saving()" (closed)="closed.emit()">
      <form id="request-form" class="stack" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <div class="field">
          <span class="label">Request type<span class="req">*</span></span>
          <div class="types" role="radiogroup" aria-label="Request type">
            @for (t of types; track t.value) {
              <label class="type" [class.active]="form.controls.type.value === t.value">
                <input type="radio" formControlName="type" [value]="t.value" class="sr-only" />
                <app-icon [name]="help[t.value].icon" [size]="18" />
                <strong>{{ t.label }}</strong>
                <span>{{ help[t.value].text }}</span>
              </label>
            }
          </div>
        </div>
        <div class="field">
          <label for="rq-asset">Asset@if (needsAsset()) { <span class="req">*</span> }</label>
          <select id="rq-asset" class="select" formControlName="assetId">
            <option [ngValue]="null">{{ needsAsset() ? 'Select one of your assets…' : 'Not about a specific asset' }}</option>
            @for (a of myAssets(); track a.id) { <option [ngValue]="a.id">{{ a.assetTag }} — {{ a.name }}</option> }
          </select>
          @if (needsAsset() && myAssets().length === 0) { <span class="hint">You don’t have any assigned assets. Choose “New Asset” or “Other”.</span> }
          <app-field-error [server]="errors()['assetId']" />
        </div>
        <div class="field">
          <label for="rq-comments">Comments<span class="req">*</span></label>
          <textarea id="rq-comments" class="textarea" formControlName="comments" maxlength="2000" rows="4" placeholder="Describe the problem or what you need…"></textarea>
          <span class="hint">{{ form.controls.comments.value?.length ?? 0 }}/2000</span>
          <app-field-error [control]="form.controls.comments" name="Comments" [server]="errors()['comments']" />
        </div>
      </form>
      <ng-container modal-footer>
        <button type="button" class="btn" (click)="closed.emit()" [disabled]="saving()">Cancel</button>
        <button type="submit" form="request-form" class="btn btn-primary" [disabled]="saving()">
          @if (saving()) { <span class="spinner"></span> } Submit request
        </button>
      </ng-container>
    </app-modal>`,
  styles: `
    .types { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 8px; }
    .type { display: flex; flex-direction: column; gap: 3px; padding: 12px; border: 1px solid var(--line); border-radius: 12px; cursor: pointer;
      transition: border-color var(--t-fast), background var(--t-fast), box-shadow var(--t-fast); color: var(--ink-3); }
    .type:hover { border-color: var(--line-strong); }
    .type.active { border-color: var(--primary); background: var(--primary-soft); box-shadow: var(--ring); color: var(--primary-ink); }
    .type strong { color: var(--ink); font-size: 13.5px; margin-top: 4px; }
    .type span { font-size: 12px; line-height: 1.35; }
    .type:has(input:focus-visible) { outline: 2px solid var(--primary); outline-offset: 2px; }
  `
})
export class RequestFormComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);

  readonly open = input(false);
  readonly presetAssetId = input<number | null>(null);
  readonly closed = output<void>();
  readonly created = output<RequestDetail>();

  protected readonly types = REQUEST_TYPES;
  protected readonly help = TYPE_HELP;
  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly myAssets = signal<AssetListItem[]>([]);
  private readonly type = signal<RequestType>('Repair');
  protected readonly needsAsset = computed(() => ['Repair', 'Replacement', 'Return'].includes(this.type()));

  protected readonly form = inject(FormBuilder).group({
    type: ['Repair' as RequestType, Validators.required],
    assetId: [null as number | null],
    comments: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(2000)]]
  });

  constructor() {
    clearServerErrorsOnChange(this.form, this.errors, inject(DestroyRef));
    this.form.controls.type.valueChanges.subscribe(t => this.type.set(t ?? 'Other'));
    effect(() => {
      if (!this.open()) return;
      const preset = this.presetAssetId();
      this.errors.set({});
      this.form.reset({ type: preset ? 'Repair' : 'NewAsset', assetId: preset, comments: '' });
      this.api.get<EmployeeProfile>('me/profile').subscribe({
        next: p => this.myAssets.set(p.assets),
        error: () => this.myAssets.set([])
      });
    });
  }

  protected save(): void {
    if (this.saving()) return;
    if (this.form.invalid) return this.form.markAllAsTouched();
    const v = this.form.getRawValue();
    if (this.needsAsset() && !v.assetId) {
      this.errors.set({ assetId: 'Select the asset this request is about.' });
      return;
    }
    this.saving.set(true);
    this.errors.set({});
    this.api.post<RequestDetail>('requests', { type: v.type, assetId: v.assetId, comments: v.comments?.trim() }).subscribe({
      next: detail => {
        this.saving.set(false);
        this.notify.success('Request submitted successfully.');
        this.created.emit(detail);
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
