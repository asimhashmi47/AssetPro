import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';
import { IconComponent } from './icon.component';

/**
 * Accessible modal dialog. Content goes in the default slot, buttons in [modal-footer].
 * Closing is blocked while `busy` is true so a save in flight can't be abandoned half-way.
 */
@Component({
  selector: 'app-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  host: { '(document:keydown.escape)': 'onEscape()' },
  template: `
    @if (open()) {
      <div class="backdrop" (click)="close()"></div>
      <div class="wrap" (click)="close()">
        <section #dialog class="dialog" [class]="'dialog ' + size()" role="dialog" aria-modal="true" [attr.aria-label]="title()"
          tabindex="-1" (click)="$event.stopPropagation()">
          <header>
            @if (icon()) { <span class="icon" [class]="'icon ' + tone()"><app-icon [name]="icon()!" [size]="20" /></span> }
            <div class="titles">
              <h2>{{ title() }}</h2>
              @if (subtitle()) { <p>{{ subtitle() }}</p> }
            </div>
            <button type="button" class="btn btn-ghost btn-icon" (click)="close()" [disabled]="busy()" aria-label="Close dialog">
              <app-icon name="x" />
            </button>
          </header>
          <div class="body"><ng-content /></div>
          <footer><ng-content select="[modal-footer]" /></footer>
        </section>
      </div>
    }`,
  styles: `
    .backdrop { position: fixed; inset: 0; z-index: 90; background: rgba(22, 26, 46, .42); backdrop-filter: blur(3px); animation: fade-in .18s ease; }
    .wrap { position: fixed; inset: 0; z-index: 91; display: flex; align-items: flex-start; justify-content: center; padding: 6vh 16px 24px; overflow-y: auto; }
    .dialog { width: 100%; max-width: 560px; background: var(--surface); border-radius: 20px; box-shadow: var(--shadow-lg);
      animation: pop-in .24s var(--ease-out); outline: none; display: flex; flex-direction: column; }
    .dialog.sm { max-width: 440px; } .dialog.lg { max-width: 760px; }
    header { display: flex; align-items: flex-start; gap: 14px; padding: 22px 22px 6px; }
    .icon { width: 42px; height: 42px; border-radius: 13px; display: grid; place-items: center; flex-shrink: 0;
      background: var(--primary-soft); color: var(--primary-strong); }
    .icon.success { background: var(--success-soft); color: var(--success-ink); }
    .icon.warning { background: var(--warning-soft); color: var(--warning-ink); }
    .icon.danger { background: var(--danger-soft); color: var(--danger-ink); }
    .icon.info { background: var(--info-soft); color: var(--info-ink); }
    .icon.violet { background: var(--violet-soft); color: var(--violet-ink); }
    .titles { flex: 1; min-width: 0; padding-top: 2px; }
    .titles p { color: var(--ink-3); font-size: 13px; margin-top: 3px; }
    .body { padding: 16px 22px 8px; }
    footer { display: flex; justify-content: flex-end; gap: 8px; padding: 14px 22px 20px; flex-wrap: wrap; }
    footer:empty { display: none; }
    @media (max-width: 560px) { .wrap { padding: 12px; } footer ::ng-deep .btn { flex: 1; } }
  `
})
export class ModalComponent {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly icon = input<string>();
  readonly tone = input<'primary' | 'success' | 'warning' | 'danger' | 'info' | 'violet'>('primary');
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  readonly busy = input(false);
  readonly closed = output<void>();

  private readonly dialog = viewChild<ElementRef<HTMLElement>>('dialog');

  constructor() {
    effect(() => {
      const el = this.dialog()?.nativeElement;
      if (this.open() && el) queueMicrotask(() => (el.querySelector<HTMLElement>('input, select, textarea') ?? el).focus());
    });
  }

  close(): void {
    if (!this.busy()) this.closed.emit();
  }

  onEscape(): void {
    if (this.open()) this.close();
  }
}
