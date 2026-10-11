import { ChangeDetectionStrategy, Component, Pipe, PipeTransform, computed, inject, input, output } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { Tone, label, statusTone } from '../core/labels';
import { ReferenceDataService } from '../core/reference-data.service';
import { IconComponent } from './icon.component';

/** Coloured status/condition pill. Pass a raw enum value; the label and tone are derived. */
@Component({
  selector: 'app-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge ' + toneClass()" [class.plain]="plain()">{{ text() }}</span>`
})
export class BadgeComponent {
  readonly value = input<string | null | undefined>();
  readonly tone = input<Tone | undefined>();
  readonly plain = input(false);
  protected readonly text = computed(() => label(this.value()));
  protected readonly toneClass = computed(() => {
    const t = this.tone() ?? statusTone(this.value());
    return t === 'neutral' ? '' : t;
  });
}

@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  template: `
    <header class="ph">
      @if (icon()) {
        <span class="ph-icon"><app-icon [name]="icon()!" [size]="22" /></span>
      }
      <div class="ph-text">
        <h1>{{ title() }}</h1>
        @if (subtitle()) { <p>{{ subtitle() }}</p> }
      </div>
      <div class="ph-actions"><ng-content /></div>
    </header>`,
  styles: `
    .ph { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .ph-icon { width: 46px; height: 46px; border-radius: 14px; display: grid; place-items: center; color: var(--primary-strong);
      background: linear-gradient(135deg, #eef0ff, #f4eeff); border: 1px solid #e3e5fb; flex-shrink: 0; }
    .ph-text { flex: 1; min-width: 200px; }
    .ph-text p { color: var(--ink-3); margin-top: 4px; font-size: 13.5px; }
    .ph-actions { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    @media (max-width: 640px) { .ph-actions { width: 100%; } }
  `
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly icon = input<string>();
}

@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  template: `
    <div class="empty" [class.compact]="compact()">
      <div class="art">
        <span class="ring"></span>
        <span class="glyph"><app-icon [name]="icon()" [size]="compact() ? 22 : 28" /></span>
      </div>
      <h3>{{ title() }}</h3>
      @if (message()) { <p>{{ message() }}</p> }
      <div class="actions"><ng-content /></div>
    </div>`,
  styles: `
    .empty { display: flex; flex-direction: column; align-items: center; text-align: center; padding: 48px 24px; gap: 8px; animation: fade-in .3s ease; }
    .empty.compact { padding: 28px 16px; }
    .art { position: relative; width: 76px; height: 76px; margin-bottom: 8px; display: grid; place-items: center; }
    .compact .art { width: 56px; height: 56px; }
    .ring { position: absolute; inset: 0; border-radius: 24px; background: linear-gradient(135deg, #eef0ff, #f6efff); transform: rotate(8deg); }
    .glyph { position: relative; width: 70%; height: 70%; border-radius: 18px; background: var(--surface); display: grid; place-items: center;
      color: var(--primary-strong); box-shadow: var(--shadow-md); }
    h3 { font-size: 16px; }
    p { color: var(--ink-3); max-width: 380px; font-size: 13.5px; }
    .actions { margin-top: 10px; display: flex; gap: 8px; }
    .actions:empty { display: none; }
  `
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly message = input<string>();
  readonly icon = input('box');
  readonly compact = input(false);
}

@Component({
  selector: 'app-error-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  template: `
    <div class="err" role="alert">
      <span class="glyph"><app-icon name="alert" [size]="24" /></span>
      <h3>{{ title() }}</h3>
      <p>{{ message() }}</p>
      <button type="button" class="btn btn-sm" (click)="retry.emit()"><app-icon name="refresh" [size]="15" /> Try again</button>
    </div>`,
  styles: `
    .err { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 8px; padding: 40px 24px; }
    .glyph { width: 52px; height: 52px; border-radius: 16px; display: grid; place-items: center; background: var(--danger-soft); color: var(--danger-ink); margin-bottom: 6px; }
    h3 { font-size: 16px; }
    p { color: var(--ink-3); max-width: 420px; font-size: 13.5px; margin-bottom: 6px; }
  `
})
export class ErrorStateComponent {
  readonly title = input('We couldn’t load this');
  readonly message = input('Something went wrong while loading. Check your connection and try again.');
  readonly retry = output<void>();
}

export type StatAccent = 'indigo' | 'emerald' | 'amber' | 'rose' | 'cyan' | 'slate' | 'violet';

@Component({
  selector: 'app-stat-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  template: `
    <article class="stat" [class]="'stat ' + accent()" [class.hero]="hero()">
      <div class="top">
        <span class="label">{{ label() }}</span>
        <span class="chip"><app-icon [name]="icon()" [size]="18" /></span>
      </div>
      @if (loading()) {
        <span class="skeleton value-skel"></span>
      } @else {
        <span class="value">{{ value() }}</span>
      }
      <span class="hint">{{ hint() }}</span>
    </article>`,
  styles: `
    .stat { --a1: #6366f1; --a2: #8b5cf6; --tint: #eef0ff;
      position: relative; overflow: hidden; display: flex; flex-direction: column; gap: 6px; min-height: 132px;
      padding: 18px 20px; border-radius: var(--r-lg); background: var(--surface); border: 1px solid var(--line); box-shadow: var(--shadow-xs);
      transition: transform var(--t-med) var(--ease-out), box-shadow var(--t-med) var(--ease-out); }
    .stat::after { content: ""; position: absolute; right: -40px; bottom: -60px; width: 160px; height: 160px; border-radius: 50%;
      background: radial-gradient(circle, var(--tint) 0%, transparent 70%); pointer-events: none; }
    .stat:hover { transform: translateY(-2px); box-shadow: var(--shadow-md); }
    .emerald { --a1: #10b981; --a2: #14b8a6; --tint: #dcf7ec; }
    .amber { --a1: #f59e0b; --a2: #f97316; --tint: #ffefd2; }
    .rose { --a1: #ec4899; --a2: #f43f5e; --tint: #fde2ef; }
    .cyan { --a1: #06b6d4; --a2: #0ea5e9; --tint: #d8f4fa; }
    .slate { --a1: #64748b; --a2: #475569; --tint: #e9ecf3; }
    .violet { --a1: #8b5cf6; --a2: #a855f7; --tint: #efe6ff; }
    .top { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .label { font-size: 13px; font-weight: 600; color: var(--ink-3); }
    .chip { width: 36px; height: 36px; border-radius: 11px; display: grid; place-items: center; color: #fff;
      background: linear-gradient(135deg, var(--a1), var(--a2)); box-shadow: 0 6px 14px -6px var(--a1); }
    .value { font: 700 30px/1.1 var(--font-display); color: var(--ink); letter-spacing: -0.02em; position: relative; z-index: 1; font-variant-numeric: tabular-nums; }
    .value-skel { width: 72px; height: 32px; }
    .hint { font-size: 12.5px; color: var(--ink-3); position: relative; z-index: 1; }
    .hero { background: linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%); border-color: transparent; box-shadow: 0 14px 30px -14px rgba(79, 70, 229, .7); }
    .hero::after { background: radial-gradient(circle, rgba(255,255,255,.18) 0%, transparent 70%); width: 220px; height: 220px; }
    .hero .label, .hero .hint { color: rgba(255,255,255,.82); }
    .hero .value { color: #fff; }
    .hero .chip { background: rgba(255,255,255,.18); box-shadow: none; backdrop-filter: blur(4px); }
  `
})
export class StatCardComponent {
  readonly label = input.required<string>();
  readonly value = input<string | number | null>(null);
  readonly hint = input('');
  readonly icon = input('box');
  readonly accent = input<StatAccent>('indigo');
  readonly hero = input(false);
  readonly loading = input(false);
}

/** Inline validation message for a reactive form control (shown once touched). */
@Component({
  selector: 'app-field-error',
  // Default change detection on purpose: touched/dirty state changes without any input changing.
  template: `@if (message()) { <span class="error" role="alert">{{ message() }}</span> }`
})
export class FieldErrorComponent {
  readonly control = input<AbstractControl | null>(null);
  readonly server = input<string | undefined>();
  readonly name = input('This field');
  // Re-evaluated on every change detection pass of the parent (OnPush with control state is fine for forms).
  protected message(): string | null {
    if (this.server()) return this.server()!;
    const c = this.control();
    if (!c || !c.errors || !(c.touched || c.dirty)) return null;
    const e = c.errors;
    if (e['required']) return `${this.name()} is required.`;
    if (e['email']) return 'Enter a valid email address.';
    if (e['minlength']) return `Use at least ${e['minlength'].requiredLength} characters.`;
    if (e['maxlength']) return `Use at most ${e['maxlength'].requiredLength} characters.`;
    if (e['min']) return `Must be at least ${e['min'].min}.`;
    if (e['max']) return `Must be at most ${e['max'].max}.`;
    if (e['pattern']) return `${this.name()} has an invalid format.`;
    if (e['futureDate']) return 'Date cannot be in the future.';
    if (e['dateOrder']) return e['dateOrder'];
    return `${this.name()} is invalid.`;
  }
}

/** Formats an ISO date/datetime in the viewer's local time zone. */
@Pipe({ name: 'localDate' })
export class LocalDatePipe implements PipeTransform {
  transform(value: string | null | undefined, mode: 'date' | 'datetime' | 'relative' = 'date'): string {
    if (!value) return '—';
    // DateOnly values ("2026-01-04") are calendar dates, not instants - don't shift them by time zone.
    const isDateOnly = /^\d{4}-\d{2}-\d{2}$/.test(value);
    const date = isDateOnly ? new Date(`${value}T00:00:00`) : new Date(value);
    if (Number.isNaN(date.getTime())) return '—';
    if (mode === 'relative') return relative(date);
    return mode === 'datetime' && !isDateOnly
      ? date.toLocaleString(undefined, { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}

export function relative(date: Date): string {
  const seconds = Math.round((Date.now() - date.getTime()) / 1000);
  const fmt = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  const steps: [number, Intl.RelativeTimeFormatUnit][] = [[60, 'second'], [60, 'minute'], [24, 'hour'], [7, 'day'], [4.35, 'week'], [12, 'month'], [Infinity, 'year']];
  let value = -seconds;
  for (const [size, unit] of steps) {
    if (Math.abs(value) < size) return fmt.format(Math.round(value), unit);
    value /= size;
  }
  return date.toLocaleDateString();
}

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private readonly ref = inject(ReferenceDataService);
  transform(value: number | null | undefined, compact = false): string {
    if (value === null || value === undefined) return '—';
    try {
      return new Intl.NumberFormat(undefined, {
        style: 'currency', currency: this.ref.settings().currency || 'USD',
        maximumFractionDigits: compact || Number.isInteger(value) ? 0 : 2,
        notation: compact ? 'compact' : 'standard'
      }).format(value);
    } catch {
      return value.toFixed(2);
    }
  }
}

@Pipe({ name: 'label' })
export class LabelPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return label(value);
  }
}
