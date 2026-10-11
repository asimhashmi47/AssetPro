import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Author credit, shown in the sign-in page and the application footer. */
@Component({
  selector: 'app-credit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="credit">
      <a href="mailto:asimhashmi47&#64;gmail.com">asimhashmi47&#64;gmail.com</a>
      <span class="sep" aria-hidden="true">|</span>
      <a href="http://masimaziz.com" target="_blank" rel="noopener noreferrer">masimaziz.com</a>
    </span>`,
  styles: `
    .credit { display: inline-flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: center; }
    a { color: inherit; font-weight: 600; }
    a:hover { color: var(--primary-strong); text-decoration: underline; }
    .sep { opacity: .45; }
  `
})
export class CreditComponent {}
