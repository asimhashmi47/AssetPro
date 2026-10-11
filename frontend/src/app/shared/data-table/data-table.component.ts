import {
  ChangeDetectionStrategy, Component, DestroyRef, Directive, OnInit, TemplateRef, computed, contentChild, contentChildren,
  inject, input, signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { Observable, Subject, debounceTime, switchMap, catchError, of, map, tap } from 'rxjs';
import { GridQuery } from '../../core/api.service';
import { PagedResult } from '../../core/models';
import { label } from '../../core/labels';
import { NotifyService } from '../../core/notify.service';
import { ReferenceDataService } from '../../core/reference-data.service';
import { IconComponent } from '../icon.component';
import { BadgeComponent, EmptyStateComponent, ErrorStateComponent, LocalDatePipe, MoneyPipe } from '../ui';
import { TableColumn } from './data-table.types';

/** <ng-template appCell="status" let-row> … </ng-template> overrides how one column renders. */
@Directive({ selector: 'ng-template[appCell]' })
export class CellDirective {
  readonly appCell = input.required<string>();
  readonly template = inject(TemplateRef<{ $implicit: unknown }>);
}

/** <ng-template appRowActions let-row> … </ng-template> renders the last (actions) column. */
@Directive({ selector: 'ng-template[appRowActions]' })
export class RowActionsDirective {
  readonly template = inject(TemplateRef<{ $implicit: unknown }>);
}

interface FilterState {
  [key: string]: string;
}

/**
 * Reusable server-side table: global search, sorting, per-column filters (text / multi-select / date and
 * number ranges), column visibility (remembered per table), paging, CSV export and loading/empty/error states.
 */
@Component({
  selector: 'app-data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, IconComponent, BadgeComponent, EmptyStateComponent, ErrorStateComponent, LocalDatePipe, MoneyPipe],
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.css',
  host: { '(document:click)': 'closeMenus($event)' }
})
export class DataTableComponent<T = any> implements OnInit {
  private readonly notify = inject(NotifyService);
  private readonly ref = inject(ReferenceDataService);
  private readonly destroyRef = inject(DestroyRef);

  readonly columns = input.required<TableColumn[]>();
  readonly fetch = input.required<(query: GridQuery) => Observable<PagedResult<T>>>();
  /** Filters always applied (not shown or clearable), e.g. a fixed scope. */
  readonly baseFilters = input<Record<string, string>>({});
  readonly defaultSort = input<string>();
  readonly defaultDir = input<'asc' | 'desc'>('asc');
  readonly storageKey = input<string>();
  readonly exportName = input('export');
  readonly searchPlaceholder = input('Search…');
  readonly emptyTitle = input('Nothing here yet');
  readonly emptyMessage = input('Records will appear here once they are created.');
  readonly emptyIcon = input('box');
  readonly rowClickable = input(false);
  readonly trackKey = input('id');

  private readonly cellTemplates = contentChildren(CellDirective);
  protected readonly actionsTemplate = contentChild(RowActionsDirective);
  protected readonly templateFor = computed(() => {
    const map = new Map<string, TemplateRef<unknown>>();
    for (const t of this.cellTemplates()) map.set(t.appCell(), t.template);
    return map;
  });

  // ---- state ----
  protected readonly rows = signal<T[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly search = signal('');
  protected readonly sort = signal<string | undefined>(undefined);
  protected readonly dir = signal<'asc' | 'desc'>('asc');
  protected readonly filters = signal<FilterState>({});
  protected readonly loading = signal(true);
  protected readonly loadedOnce = signal(false);
  protected readonly error = signal(false);
  protected readonly exporting = signal(false);
  protected readonly showFilters = signal(false);
  protected readonly columnsMenu = signal(false);
  protected readonly openEnumFilter = signal<string | null>(null);
  /** Enum filter popovers are position:fixed so the scrolling table body cannot clip them. */
  protected readonly enumMenuPos = signal<{ top: number; left: number }>({ top: 0, left: 0 });
  protected readonly hiddenKeys = signal<Set<string>>(new Set());

  protected readonly pageSizes = [10, 25, 50, 100];
  protected readonly visibleColumns = computed(() => this.columns().filter(c => !this.hiddenKeys().has(c.key)));
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly rangeStart = computed(() => (this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));
  protected readonly rangeEnd = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
  protected readonly activeFilterCount = computed(() => Object.values(this.filters()).filter(v => v && v !== '..').length);
  protected readonly hasCriteria = computed(() => this.activeFilterCount() > 0 || this.search().trim().length > 0);
  protected readonly pages = computed(() => pageWindow(this.page(), this.pageCount()));
  protected readonly skeletonRows = Array.from({ length: 6 }, (_, i) => i);

  private readonly trigger = new Subject<{ debounce: boolean }>();

  ngOnInit(): void {
    this.pageSize.set(this.ref.settings().defaultPageSize || 10);
    this.sort.set(this.defaultSort());
    this.dir.set(this.defaultDir());
    this.restoreColumns();

    this.trigger.pipe(
      // Typing debounces; clicks (sort/page/filters) go through after a single frame.
      debounceTime(0),
      switchMap(({ debounce }) => (debounce ? of(null).pipe(debounceTime(300)) : of(null))),
      tap(() => { this.loading.set(true); this.error.set(false); }),
      switchMap(() => this.fetch()(this.query()).pipe(
        map(result => ({ result, failed: false })),
        catchError(() => of({ result: null, failed: true }))
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ result, failed }) => {
      this.loading.set(false);
      this.loadedOnce.set(true);
      if (failed || !result) {
        this.error.set(true);
        return;
      }
      this.rows.set(result.items);
      this.total.set(result.totalCount);
      // If rows were removed and the page no longer exists, step back.
      if (result.items.length === 0 && this.page() > 1 && result.totalCount > 0) {
        this.page.set(Math.ceil(result.totalCount / this.pageSize()));
        this.reload();
      }
    });

    this.reload();
  }

  /** Re-runs the current query (call after creating/updating rows). */
  reload(): void {
    this.trigger.next({ debounce: false });
  }

  /** Apply a filter programmatically (e.g. from status chips above the table). */
  setFilter(key: string, value: string): void {
    this.filters.update(f => ({ ...f, [key]: value }));
    this.page.set(1);
    this.reload();
  }

  filterValue(key: string): string {
    return this.filters()[key] ?? '';
  }

  // ---- interactions ----
  protected onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
    this.trigger.next({ debounce: true });
  }

  protected toggleSort(col: TableColumn): void {
    if (col.sortable === false) return;
    if (this.sort() !== col.key) {
      this.sort.set(col.key);
      this.dir.set(col.format === 'date' || col.format === 'datetime' ? 'desc' : 'asc');
    } else if (this.dir() === 'asc') {
      this.dir.set('desc');
    } else {
      this.sort.set(this.defaultSort());
      this.dir.set(this.defaultDir());
    }
    this.page.set(1);
    this.reload();
  }

  protected ariaSort(col: TableColumn): 'ascending' | 'descending' | 'none' {
    return this.sort() === col.key ? (this.dir() === 'asc' ? 'ascending' : 'descending') : 'none';
  }

  protected onTextFilter(key: string, value: string): void {
    this.filters.update(f => ({ ...f, [key]: value }));
    this.page.set(1);
    this.trigger.next({ debounce: true });
  }

  protected onRangeFilter(key: string, part: 'from' | 'to', value: string): void {
    const [from, to] = (this.filters()[key] ?? '..').split('..');
    const next = part === 'from' ? `${value}..${to ?? ''}` : `${from ?? ''}..${value}`;
    this.filters.update(f => ({ ...f, [key]: next === '..' ? '' : next }));
    this.page.set(1);
    this.trigger.next({ debounce: true });
  }

  protected rangePart(key: string, part: 'from' | 'to'): string {
    const [from, to] = (this.filters()[key] ?? '').split('..');
    return (part === 'from' ? from : to) ?? '';
  }

  protected selectedOptions(key: string): Set<string> {
    return new Set((this.filters()[key] ?? '').split(',').filter(Boolean));
  }

  protected toggleOption(key: string, value: string): void {
    const set = this.selectedOptions(key);
    if (set.has(value)) set.delete(value); else set.add(value);
    this.filters.update(f => ({ ...f, [key]: [...set].join(',') }));
    this.page.set(1);
    this.reload();
  }

  protected enumSummary(col: TableColumn): string {
    const set = this.selectedOptions(col.key);
    if (set.size === 0) return 'All';
    if (set.size === 1) return label([...set][0]);
    return `${set.size} selected`;
  }

  protected onBoolFilter(key: string, value: string): void {
    this.filters.update(f => ({ ...f, [key]: value }));
    this.page.set(1);
    this.reload();
  }

  protected clearFilters(): void {
    this.filters.set({});
    this.search.set('');
    this.page.set(1);
    this.reload();
  }

  protected goTo(page: number): void {
    const target = Math.min(Math.max(1, page), this.pageCount());
    if (target === this.page()) return;
    this.page.set(target);
    this.reload();
  }

  protected changePageSize(size: string): void {
    this.pageSize.set(Number(size));
    this.page.set(1);
    this.reload();
  }

  protected toggleColumn(col: TableColumn): void {
    if (col.locked) return;
    const next = new Set(this.hiddenKeys());
    if (next.has(col.key)) next.delete(col.key);
    else if (this.visibleColumns().length > 1) next.add(col.key);
    this.hiddenKeys.set(next);
    this.saveColumns();
  }

  protected resetColumns(): void {
    this.hiddenKeys.set(new Set(this.columns().filter(c => c.hidden).map(c => c.key)));
    this.saveColumns();
  }

  protected toggleMenu(event: Event, which: 'columns' | string): void {
    event.stopPropagation();
    if (which === 'columns') {
      this.openEnumFilter.set(null);
      this.columnsMenu.update(v => !v);
    } else {
      this.columnsMenu.set(false);
      const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
      this.enumMenuPos.set({ top: rect.bottom + 6, left: Math.min(rect.left, window.innerWidth - 220) });
      this.openEnumFilter.update(v => (v === which ? null : which));
    }
  }

  protected closeMenus(event: Event): void {
    const target = event.target as HTMLElement | null;
    if (target?.closest('.menu')) return;
    this.columnsMenu.set(false);
    this.openEnumFilter.set(null);
  }

  protected trackRow = (index: number, row: T): unknown => (row as Record<string, unknown>)[this.trackKey()] ?? index;

  protected value(row: T, key: string): any {
    return (row as Record<string, unknown>)[key];
  }

  protected filterKind(col: TableColumn): 'text' | 'enum' | 'date' | 'number' | 'bool' | 'none' {
    if (col.filterable === false) return 'none';
    switch (col.format) {
      case 'badge': return col.options?.length ? 'enum' : 'text';
      case 'date': case 'datetime': return 'date';
      case 'money': case 'number': return 'number';
      case 'bool': return 'bool';
      default: return 'text';
    }
  }

  // ---- export ----
  protected exportCsv(): void {
    if (this.exporting()) return;
    this.exporting.set(true);
    const query = { ...this.query(), page: 1, pageSize: 5000 };
    this.fetch()(query).subscribe({
      next: result => {
        this.exporting.set(false);
        const cols = this.visibleColumns();
        const header = cols.map(c => csvCell(c.label)).join(',');
        const lines = result.items.map(row => cols.map(c => csvCell(this.exportCell(row, c))).join(','));
        const blob = new Blob(['﻿' + [header, ...lines].join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.exportName()}-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        const capped = result.totalCount > result.items.length ? ` (first ${result.items.length} of ${result.totalCount})` : '';
        this.notify.success(`Exported ${result.items.length} rows${capped}.`);
      },
      error: err => {
        this.exporting.set(false);
        this.notify.apiError(err, 'Export failed');
      }
    });
  }

  private exportCell(row: T, col: TableColumn): string {
    if (col.exportValue) return `${col.exportValue(row) ?? ''}`;
    const v = this.value(row, col.key);
    if (v === null || v === undefined) return '';
    switch (col.format) {
      case 'badge': return label(String(v));
      case 'date': return new LocalDatePipe().transform(String(v), 'date');
      case 'datetime': return new LocalDatePipe().transform(String(v), 'datetime');
      case 'bool': return v ? 'Yes' : 'No';
      default: return String(v);
    }
  }

  private query(): GridQuery {
    return {
      page: this.page(),
      pageSize: this.pageSize(),
      search: this.search(),
      sort: this.sort(),
      dir: this.dir(),
      filters: { ...this.filters(), ...this.baseFilters() }
    };
  }

  // ---- column persistence (per-viewer convenience; safe to lose) ----
  private restoreColumns(): void {
    const defaults = new Set(this.columns().filter(c => c.hidden).map(c => c.key));
    const key = this.storageKey();
    if (!key) return this.hiddenKeys.set(defaults);
    try {
      const raw = localStorage.getItem(`assetpro.cols.${key}`);
      const saved = raw ? (JSON.parse(raw) as string[]) : null;
      this.hiddenKeys.set(saved ? new Set(saved.filter(k => this.columns().some(c => c.key === k && !c.locked))) : defaults);
    } catch {
      this.hiddenKeys.set(defaults);
    }
  }

  private saveColumns(): void {
    const key = this.storageKey();
    if (!key) return;
    try {
      localStorage.setItem(`assetpro.cols.${key}`, JSON.stringify([...this.hiddenKeys()]));
    } catch { /* ignore */ }
  }
}

function csvCell(value: string | number): string {
  let text = `${value ?? ''}`;
  // Neutralise spreadsheet formula injection.
  if (/^[=+\-@\t\r]/.test(text)) text = `'${text}`;
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function pageWindow(current: number, count: number): (number | '…')[] {
  if (count <= 7) return Array.from({ length: count }, (_, i) => i + 1);
  const pages: (number | '…')[] = [1];
  const start = Math.max(2, current - 1);
  const end = Math.min(count - 1, current + 1);
  if (start > 2) pages.push('…');
  for (let p = start; p <= end; p++) pages.push(p);
  if (end < count - 1) pages.push('…');
  pages.push(count);
  return pages;
}
