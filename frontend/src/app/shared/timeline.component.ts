import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AssetHistoryItem, RequestStatus, RequestStatusHistory } from '../core/models';
import { REQUEST_FLOW, actionSentence, actionVisual, label } from '../core/labels';
import { IconComponent } from './icon.component';
import { BadgeComponent, EmptyStateComponent, LocalDatePipe } from './ui';

/** Vertical asset-history timeline (used on asset detail, employee profile and the history page). */
@Component({
  selector: 'app-history-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent, BadgeComponent, EmptyStateComponent, LocalDatePipe, RouterLink],
  template: `
    @if (items().length === 0) {
      <app-empty-state title="No asset history yet" message="Assignments, returns, repairs and replacements will show up here." icon="history" [compact]="true" />
    } @else {
      <ol class="tl">
        @for (h of items(); track h.id) {
          @let vis = visual(h);
          <li class="item" [style.animation-delay.ms]="$index < 12 ? $index * 30 : 0">
            <span class="dot" [class]="'dot ' + vis.tone"><app-icon [name]="vis.icon" [size]="15" [stroke]="2.1" /></span>
            <div class="content">
              <div class="head">
                <span class="title">{{ sentence(h) }}</span>
                <time class="when" [attr.datetime]="h.occurredAt" [title]="h.occurredAt | localDate: 'datetime'">{{ h.occurredAt | localDate: 'relative' }}</time>
              </div>
              <div class="meta">
                @if (showAsset()) { <a class="tag" [routerLink]="['/assets', h.assetId]">{{ h.assetTag }}</a> }
                @if (h.previousStatus && h.newStatus && h.previousStatus !== h.newStatus) {
                  <span class="transition"><app-badge [value]="h.previousStatus" /> <app-icon name="arrow-right" [size]="13" /> <app-badge [value]="h.newStatus" /></span>
                }
                @if (h.performedBy) { <span class="by">by {{ h.performedBy }}</span> }
              </div>
              @if (h.notes) { <p class="notes">{{ h.notes }}</p> }
            </div>
          </li>
        }
      </ol>
    }`,
  styles: `
    .tl { list-style: none; margin: 0; padding: 0; position: relative; }
    .item { position: relative; display: flex; gap: 14px; padding-bottom: 20px; animation: pop-in .3s var(--ease-out) both; }
    .item:not(:last-child)::before { content: ""; position: absolute; left: 15px; top: 34px; bottom: 2px; width: 2px; border-radius: 2px; background: var(--line); }
    .dot { width: 32px; height: 32px; border-radius: 10px; display: grid; place-items: center; flex-shrink: 0; background: var(--slate-soft); color: var(--slate-ink); }
    .dot.success { background: var(--success-soft); color: var(--success-ink); }
    .dot.primary { background: var(--primary-soft); color: var(--primary-ink); }
    .dot.warning { background: var(--warning-soft); color: var(--warning-ink); }
    .dot.danger { background: var(--danger-soft); color: var(--danger-ink); }
    .dot.info { background: var(--info-soft); color: var(--info-ink); }
    .dot.violet { background: var(--violet-soft); color: var(--violet-ink); }
    .content { flex: 1; min-width: 0; padding-top: 5px; }
    .head { display: flex; gap: 12px; align-items: baseline; justify-content: space-between; }
    .title { font-weight: 600; color: var(--ink); font-size: 13.5px; }
    .when { font-size: 12px; color: var(--ink-4); white-space: nowrap; }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 6px; font-size: 12.5px; color: var(--ink-3); }
    .transition { display: inline-flex; align-items: center; gap: 4px; color: var(--ink-4); }
    .notes { margin-top: 8px; font-size: 13px; color: var(--ink-2); background: var(--surface-2); border: 1px solid var(--line); border-radius: 10px; padding: 8px 12px; overflow-wrap: anywhere; }
  `
})
export class HistoryTimelineComponent {
  readonly items = input<AssetHistoryItem[]>([]);
  readonly showAsset = input(true);
  protected visual = (h: AssetHistoryItem) => actionVisual(h.action);
  protected sentence = (h: AssetHistoryItem) => actionSentence(h);
}

interface Step {
  status: RequestStatus;
  label: string;
  state: 'done' | 'current' | 'upcoming' | 'rejected';
  at?: string;
  by?: string;
  note?: string | null;
}

/** Request workflow stepper: completed stages show who/when; the current stage is highlighted. */
@Component({
  selector: 'app-request-stepper',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent, LocalDatePipe],
  template: `
    <ol class="steps">
      @for (s of steps(); track s.status) {
        <li class="step" [class]="'step ' + s.state">
          <span class="marker">
            @switch (s.state) {
              @case ('done') { <app-icon name="check" [size]="14" [stroke]="2.6" /> }
              @case ('rejected') { <app-icon name="x" [size]="14" [stroke]="2.6" /> }
              @default { <span class="pip"></span> }
            }
          </span>
          <div class="text">
            <span class="name">{{ s.label }}</span>
            @if (s.at) {
              <span class="when">{{ s.at | localDate: 'datetime' }}@if (s.by) { · {{ s.by }} }</span>
            } @else if (s.state === 'current') {
              <span class="when">Waiting for this step</span>
            }
            @if (s.note) { <span class="note">“{{ s.note }}”</span> }
          </div>
        </li>
      }
    </ol>`,
  styles: `
    .steps { list-style: none; margin: 0; padding: 0; }
    .step { position: relative; display: flex; gap: 14px; padding-bottom: 18px; }
    .step:not(:last-child)::before { content: ""; position: absolute; left: 13px; top: 30px; bottom: 0; width: 2px; background: var(--line); border-radius: 2px; }
    .step.done:not(:last-child)::before { background: linear-gradient(var(--success), #6ee7b7); }
    .marker { width: 28px; height: 28px; border-radius: 50%; display: grid; place-items: center; flex-shrink: 0;
      background: var(--surface); border: 2px solid var(--line-strong); color: var(--ink-4); transition: all var(--t-med); }
    .pip { width: 8px; height: 8px; border-radius: 50%; background: currentColor; }
    .done .marker { background: var(--success); border-color: var(--success); color: #fff; }
    .current .marker { background: var(--primary); border-color: var(--primary); color: #fff; animation: pulse-ring 1.6s ease-out infinite; }
    .rejected .marker { background: var(--danger); border-color: var(--danger); color: #fff; }
    .text { display: flex; flex-direction: column; gap: 2px; padding-top: 3px; min-width: 0; }
    .name { font-weight: 600; font-size: 13.5px; color: var(--ink); }
    .upcoming .name { color: var(--ink-4); font-weight: 500; }
    .current .name { color: var(--primary-ink); }
    .rejected .name { color: var(--danger-ink); }
    .when { font-size: 12px; color: var(--ink-3); }
    .note { font-size: 12.5px; color: var(--ink-2); font-style: italic; overflow-wrap: anywhere; }
  `
})
export class RequestStepperComponent {
  readonly status = input.required<RequestStatus>();
  readonly history = input<RequestStatusHistory[]>([]);

  protected readonly steps = computed<Step[]>(() => {
    const status = this.status();
    const reached = new Map(this.history().map(h => [h.toStatus, h]));
    if (status === 'Rejected') {
      const submitted = reached.get('Submitted');
      const rejected = reached.get('Rejected');
      return [
        { status: 'Submitted', label: 'Submitted', state: 'done', at: submitted?.changedAt, by: submitted?.changedBy },
        { status: 'Rejected', label: 'Rejected', state: 'rejected', at: rejected?.changedAt, by: rejected?.changedBy, note: rejected?.note }
      ];
    }
    const currentIndex = REQUEST_FLOW.indexOf(status);
    return REQUEST_FLOW.map((s, i) => {
      const h = reached.get(s);
      const state: Step['state'] = i < currentIndex || (i === currentIndex && s === 'Done') ? 'done' : i === currentIndex ? 'current' : 'upcoming';
      return { status: s, label: label(s), state, at: i <= currentIndex ? h?.changedAt : undefined, by: h?.changedBy, note: h?.note };
    });
  });
}
