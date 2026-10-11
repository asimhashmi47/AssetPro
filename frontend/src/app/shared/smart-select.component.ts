import { ChangeDetectionStrategy, Component, ElementRef, OnInit, forwardRef, inject, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Lookup } from '../core/models';
import { LOOKUP_LABELS, LookupKind, ReferenceDataService } from '../core/reference-data.service';
import { NotifyService, fieldErrorsOf, messageOf } from '../core/notify.service';
import { ModalComponent } from './modal.component';

const ADD_NEW = '__add_new__';

/**
 * Master-data dropdown with an "Add new …" entry at the bottom. Adding persists the value,
 * refreshes the list and selects the new item automatically.
 */
@Component({
  selector: 'app-smart-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ModalComponent],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SmartSelectComponent), multi: true }],
  template: `
    <select #select class="select" [id]="inputId()" [disabled]="disabled()" [value]="current() ?? ''" (change)="onSelect($any($event.target).value)" (blur)="onTouched()">
      <option value="">{{ placeholder() }}</option>
      @for (item of items(); track item.id) {
        <option [value]="item.id" [selected]="item.id === current()">{{ item.name }}</option>
      }
      @if (allowAdd()) {
        <option disabled>──────────</option>
        <option [value]="ADD_NEW">＋ Add new {{ noun().toLowerCase() }}…</option>
      }
    </select>

    <app-modal [open]="adding()" [title]="'Add ' + noun()" icon="plus" size="sm" [busy]="saving()" (closed)="cancelAdd()">
      <!-- Not a <form>: this dialog usually lives inside the parent form, and forms cannot nest. -->
      <div class="stack">
        <div class="field">
          <label for="smart-new-name">{{ noun() }} name<span class="req">*</span></label>
          <input id="smart-new-name" class="input" name="name" [(ngModel)]="newName" [ngModelOptions]="{ standalone: true }" maxlength="100" autocomplete="off"
            [placeholder]="'e.g. ' + example()" (keydown.enter)="$event.preventDefault(); save()" />
          @if (error()) { <span class="error">{{ error() }}</span> }
        </div>
        @if (kind() === 'asset-types') {
          <div class="field">
            <label for="smart-new-prefix">Tag prefix</label>
            <input id="smart-new-prefix" class="input" name="prefix" [(ngModel)]="newPrefix" [ngModelOptions]="{ standalone: true }" maxlength="6" placeholder="Auto (first 3 letters)"
              (keydown.enter)="$event.preventDefault(); save()" />
            <span class="hint">Used to generate asset tags, e.g. LAP-00012.</span>
          </div>
        }
      </div>
      <ng-container modal-footer>
        <button type="button" class="btn" (click)="cancelAdd()" [disabled]="saving()">Cancel</button>
        <button type="button" class="btn btn-primary" (click)="save()" [disabled]="saving() || !newName.trim()">
          @if (saving()) { <span class="spinner"></span> } Save {{ noun().toLowerCase() }}
        </button>
      </ng-container>
    </app-modal>`
})
export class SmartSelectComponent implements ControlValueAccessor, OnInit {
  private readonly ref = inject(ReferenceDataService);
  private readonly notify = inject(NotifyService);

  readonly kind = input.required<LookupKind>();
  readonly placeholder = input('Select…');
  readonly allowAdd = input(true);
  readonly inputId = input<string>();

  protected readonly ADD_NEW = ADD_NEW;
  protected readonly items = signal<Lookup[]>([]);
  protected readonly current = signal<number | null>(null);
  protected readonly disabled = signal(false);
  protected readonly adding = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected newName = '';
  protected newPrefix = '';

  private readonly select = viewChild<ElementRef<HTMLSelectElement>>('select');
  private onChange: (value: number | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  protected noun = () => LOOKUP_LABELS[this.kind()];
  protected example = () => ({ departments: 'Marketing', designations: 'Team Lead', locations: 'Branch Office', brands: 'Asus', 'asset-types': 'Projector' })[this.kind()];

  ngOnInit(): void {
    this.load();
  }

  writeValue(value: number | null): void {
    this.current.set(value ?? null);
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected onSelect(raw: string): void {
    if (raw === ADD_NEW) {
      // Keep showing the previous selection while the add dialog is open.
      const el = this.select()?.nativeElement;
      if (el) el.value = this.current()?.toString() ?? '';
      this.newName = '';
      this.newPrefix = '';
      this.error.set(null);
      this.adding.set(true);
      return;
    }
    const value = raw ? Number(raw) : null;
    this.current.set(value);
    this.onChange(value);
  }

  protected cancelAdd(): void {
    this.adding.set(false);
  }

  protected save(): void {
    const name = this.newName.trim();
    if (!name || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.ref.create(this.kind(), name, this.newPrefix.trim()).subscribe({
      next: created => {
        this.saving.set(false);
        this.adding.set(false);
        this.load(created.id);
        this.notify.success(`${this.noun()} “${created.name}” added.`);
      },
      error: err => {
        this.saving.set(false);
        this.error.set(fieldErrorsOf(err)['name'] ?? messageOf(err));
      }
    });
  }

  private load(select?: number): void {
    this.ref.lookups(this.kind()).subscribe({
      next: items => {
        this.items.set(items.filter(i => i.isActive || i.id === this.current()));
        if (select !== undefined) {
          this.current.set(select);
          this.onChange(select);
        }
      },
      error: () => this.items.set([])
    });
  }
}
