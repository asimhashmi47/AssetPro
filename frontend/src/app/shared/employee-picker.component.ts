import { ChangeDetectionStrategy, Component, ElementRef, OnInit, forwardRef, inject, input, output, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../core/api.service';
import { EmployeeOption } from '../core/models';
import { IconComponent } from './icon.component';

/**
 * One searchable dropdown for picking an active employee: type to filter, arrow keys to move
 * through the matches, Enter or click to choose. Highlighting never changes the value on its
 * own — only Enter or a click commits — so arrowing past a name cannot select it by accident.
 */
@Component({
  selector: 'app-employee-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => EmployeePickerComponent), multi: true }],
  host: { '(document:pointerdown)': 'onDocumentDown($event)' },
  template: `
    <div class="combo">
      <div class="control">
        <app-icon name="search" class="lead" />
        <input #input
          class="input"
          type="text"
          role="combobox"
          autocomplete="off"
          [id]="inputId()"
          [disabled]="disabled()"
          [placeholder]="placeholder()"
          [value]="text()"
          [attr.aria-expanded]="open()"
          [attr.aria-controls]="listId"
          [attr.aria-activedescendant]="open() && active() >= 0 ? listId + '-' + active() : null"
          aria-autocomplete="list"
          (input)="onType($any($event.target).value)"
          (focus)="openMenu()"
          (keydown)="onKey($event)" />

        @if (selected(); as s) {
          <button type="button" class="icon-btn clear" (click)="clear()" [disabled]="disabled()" [attr.aria-label]="'Clear ' + s.fullName">
            <app-icon name="x" />
          </button>
        } @else {
          <app-icon name="chevron-down" class="trail" />
        }
      </div>

      @if (open()) {
        <div class="menu" [id]="listId" role="listbox" aria-label="Employees">
          @for (e of options(); track e.id; let i = $index) {
            <button type="button" role="option" [id]="listId + '-' + i"
              [class.active]="i === active()" [class.chosen]="e.id === value()"
              [attr.aria-selected]="e.id === value()"
              (pointerenter)="active.set(i)" (click)="choose(e)">
              <span class="who">
                <strong>{{ e.fullName }}</strong>
                @if (e.department) { <span class="dept">{{ e.department }}</span> }
              </span>
              <span class="code">{{ e.employeeCode }}</span>
            </button>
          } @empty {
            <p class="empty">
              @if (loading()) { Searching… } @else { No active employee matches “{{ query() }}”. }
            </p>
          }
        </div>
      }
    </div>`,
  styles: `
    .combo { position: relative; }
    .control { position: relative; display: flex; align-items: center; }
    .control .input { padding-left: 36px; padding-right: 36px; width: 100%; }
    .lead { position: absolute; left: 11px; width: 16px; height: 16px; color: var(--ink-4); pointer-events: none; }
    .trail { position: absolute; right: 11px; width: 16px; height: 16px; color: var(--ink-4); pointer-events: none; }
    .clear { position: absolute; right: 7px; width: 24px; height: 24px; border: 0; border-radius: 7px; background: transparent;
      color: var(--ink-3); display: grid; place-items: center; cursor: pointer; }
    .clear:hover { background: var(--surface-3); color: var(--ink); }
    .clear app-icon { width: 14px; height: 14px; }

    .menu { position: absolute; z-index: 20; top: calc(100% + 6px); left: 0; right: 0; max-height: 244px; overflow-y: auto;
      background: var(--surface); border: 1px solid var(--line); border-radius: 12px; box-shadow: var(--shadow-lg); padding: 4px; }
    .menu button { display: flex; width: 100%; align-items: center; gap: 10px; padding: 8px 10px; border: 0; border-radius: 9px;
      background: transparent; text-align: left; cursor: pointer; font: inherit; color: var(--ink); }
    .menu button.active { background: var(--surface-3); }
    .menu button.chosen { background: var(--primary-soft); color: var(--primary-ink); }
    .who { display: flex; flex-direction: column; gap: 1px; min-width: 0; }
    .who strong { font-size: 13.5px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dept { font-size: 12px; color: var(--ink-3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .code { font-family: var(--font-mono); font-size: 11.5px; color: var(--ink-3); margin-left: auto; flex-shrink: 0; }
    .empty { padding: 14px; text-align: center; color: var(--ink-3); font-size: 13px; }
  `
})
export class EmployeePickerComponent implements ControlValueAccessor, OnInit {
  private readonly api = inject(ApiService);
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly placeholder = input('Search by name, code or email…');
  readonly inputId = input<string>();
  /** Emits the chosen employee so callers can name them in confirmations. */
  readonly picked = output<EmployeeOption | null>();

  protected readonly listId = `emp-list-${nextId++}`;
  protected readonly options = signal<EmployeeOption[]>([]);
  protected readonly selected = signal<EmployeeOption | null>(null);
  protected readonly value = signal<number | null>(null);
  protected readonly disabled = signal(false);
  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(-1);
  /** What the input shows: the live query while searching, otherwise the chosen name. */
  protected readonly text = signal('');

  private readonly input = viewChild<ElementRef<HTMLInputElement>>('input');
  private readonly search$ = new Subject<string>();
  private onChange: (v: number | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  constructor() {
    this.search$.pipe(
      debounceTime(220),
      distinctUntilChanged(),
      switchMap(term => {
        this.loading.set(true);
        return this.api.get<EmployeeOption[]>('employees/options', { search: term }).pipe(catchError(() => of([])));
      }),
      takeUntilDestroyed()
    ).subscribe(list => {
      this.loading.set(false);
      this.options.set(list);
      this.active.set(list.length > 0 ? 0 : -1);
    });
  }

  ngOnInit(): void {
    this.search$.next('');
  }

  writeValue(v: number | null): void {
    this.value.set(v ?? null);
    if (v === null || v === undefined) {
      this.selected.set(null);
      this.text.set('');
      return;
    }
    const known = this.options().find(o => o.id === v) ?? this.selected();
    if (known?.id === v) {
      this.selected.set(known);
      this.text.set(label(known));
    }
  }

  registerOnChange(fn: (v: number | null) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(d: boolean): void { this.disabled.set(d); }

  protected openMenu(): void {
    if (this.disabled() || this.open()) return;
    this.open.set(true);
    // Start from the full list so the dropdown is useful before anything is typed.
    this.query.set('');
    this.text.set('');
    this.search$.next('');
  }

  protected onType(term: string): void {
    this.text.set(term);
    this.query.set(term);
    this.open.set(true);
    this.search$.next(term);
  }

  protected onKey(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.open()) { this.openMenu(); return; }
        this.move(1);
        return;
      case 'ArrowUp':
        event.preventDefault();
        this.move(-1);
        return;
      case 'Enter': {
        if (!this.open()) return;
        // Stop the dialog's form from submitting on the keystroke that picks someone.
        event.preventDefault();
        const choice = this.options()[this.active()];
        if (choice) this.choose(choice);
        return;
      }
      case 'Escape':
        if (!this.open()) return;
        // The dialog also closes on Escape; the first press should only dismiss this dropdown.
        event.preventDefault();
        event.stopPropagation();
        this.close();
        return;
      case 'Tab':
        this.close();
    }
  }

  protected choose(e: EmployeeOption): void {
    this.selected.set(e);
    this.value.set(e.id);
    this.text.set(label(e));
    this.onChange(e.id);
    this.picked.emit(e);
    this.close();
    this.input()?.nativeElement.focus();
  }

  protected clear(): void {
    this.selected.set(null);
    this.value.set(null);
    this.text.set('');
    this.query.set('');
    this.onChange(null);
    this.picked.emit(null);
    this.input()?.nativeElement.focus();
  }

  protected onDocumentDown(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) this.close();
  }

  private move(delta: number): void {
    const count = this.options().length;
    if (count === 0) return;
    const next = this.active() < 0 ? (delta > 0 ? 0 : count - 1) : (this.active() + delta + count) % count;
    this.active.set(next);
    document.getElementById(`${this.listId}-${next}`)?.scrollIntoView({ block: 'nearest' });
  }

  private close(): void {
    if (!this.open()) return;
    this.open.set(false);
    this.active.set(-1);
    // Leaving the field half-typed shows the chosen name again, not the abandoned search.
    this.text.set(this.selected() ? label(this.selected()!) : '');
    this.onTouched();
  }
}

let nextId = 0;

const label = (e: EmployeeOption) => `${e.fullName} · ${e.employeeCode}`;
