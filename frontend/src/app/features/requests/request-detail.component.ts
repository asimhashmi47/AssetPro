import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { avatarClass, initials, label } from '../../core/labels';
import { RequestDetail } from '../../core/models';
import { NotifyService, messageOf } from '../../core/notify.service';
import { IconComponent } from '../../shared/icon.component';
import { RequestStepperComponent } from '../../shared/timeline.component';
import { BadgeComponent, EmptyStateComponent, ErrorStateComponent, LocalDatePipe } from '../../shared/ui';

interface ActionMeta { label: string; icon: string; style: 'primary' | 'danger' | 'success' | 'default'; confirm: string; success: string; }

const ACTIONS: Record<string, ActionMeta> = {
  approve: { label: 'Approve', icon: 'check', style: 'success', confirm: 'Approve this request?', success: 'Request approved.' },
  reject: { label: 'Reject', icon: 'x', style: 'danger', confirm: 'Reject this request?', success: 'Request rejected.' },
  start: { label: 'Start processing', icon: 'play', style: 'primary', confirm: 'Start working on this request?', success: 'Request is now in progress.' },
  deliver: { label: 'Mark delivered', icon: 'truck', style: 'primary', confirm: 'Mark as delivered to the employee?', success: 'Marked as delivered.' },
  check: { label: 'I’m checking it', icon: 'search', style: 'primary', confirm: 'Start checking what was delivered?', success: 'Thanks — status set to Checking.' },
  receive: { label: 'Confirm received', icon: 'check-circle', style: 'success', confirm: 'Confirm you received it and are satisfied?', success: 'Thanks for confirming!' },
  complete: { label: 'Close request', icon: 'flag', style: 'success', confirm: 'Close this request as done?', success: 'Request closed.' }
};

@Component({
  selector: 'app-request-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, RequestStepperComponent, BadgeComponent, EmptyStateComponent, ErrorStateComponent, LocalDatePipe],
  template: `
    <div class="page">
      <a class="back" routerLink="/requests"><app-icon name="arrow-left" [size]="15" /> All requests</a>
      @if (error() === 'missing') {
        <div class="card"><app-empty-state title="Request not found" icon="search" message="It may not exist, or it belongs to someone else." /></div>
      } @else if (error() === 'failed') {
        <div class="card"><app-error-state (retry)="load()" /></div>
      } @else if (detail(); as d) {
        @let r = d.request;
        <section class="card hero">
          <div class="hero-left">
            <div class="row wrap">
              <span class="tag big">{{ r.requestNumber }}</span>
              <app-badge [value]="r.status" />
              <app-badge [value]="r.type" [plain]="true" tone="neutral" />
            </div>
            <h1>{{ typeLabel(r.type) }} request@if (r.assetName) { <span class="muted"> · {{ r.assetName }}</span> }</h1>
            <p class="muted">Submitted {{ r.createdAt | localDate: 'datetime' }} · updated {{ r.updatedAt | localDate: 'relative' }}</p>
          </div>
          <div class="hero-actions">
            @for (a of actions(); track a.key) {
              <button type="button" class="btn btn-sm" [class.btn-primary]="a.style === 'primary'" [class.btn-danger]="a.style === 'danger'" [class.btn-success]="a.style === 'success'"
                [disabled]="busy()" (click)="run(a.key)">
                @if (busy() === a.key) { <span class="spinner"></span> } @else { <app-icon [name]="a.icon" [size]="15" /> } {{ a.label }}
              </button>
            } @empty {
              @if (r.status !== 'Done' && r.status !== 'Rejected') { <span class="waiting"><app-icon name="clock" [size]="15" /> Waiting on {{ waitingOn() }}</span> }
            }
            @if (auth.isAdmin()) {
              <button type="button" class="btn btn-sm btn-ghost danger" [disabled]="!!busy()" (click)="remove(r)">
                <app-icon name="trash" [size]="15" /> Delete
              </button>
            }
          </div>
        </section>

        <div class="layout">
          <article class="card">
            <div class="card-head"><h2>Details</h2></div>
            <div class="card-body">
              <div class="person">
                <span class="avatar" [class]="'avatar ' + avatarClass(r.requesterName)">{{ initials(r.requesterName) }}</span>
                <div><strong>{{ r.requesterName }}</strong><span>{{ r.department ?? 'No department' }}</span></div>
              </div>
              <dl class="kv">
                <dt>Type</dt><dd>{{ typeLabel(r.type) }}</dd>
                <dt>Asset</dt>
                <dd>@if (r.assetId) { <a class="tag" [routerLink]="['/assets', r.assetId]">{{ r.assetTag }}</a> {{ r.assetName }} } @else { — }</dd>
                <dt>Status</dt><dd><app-badge [value]="r.status" /></dd>
              </dl>
              <span class="section-title" style="display:block; margin: 20px 0 8px">Comments</span>
              <p class="comments">{{ r.comments }}</p>
            </div>
          </article>
          <article class="card">
            <div class="card-head"><div><h2>Progress</h2><span class="sub">Every stage is timestamped</span></div></div>
            <div class="card-body"><app-request-stepper [status]="r.status" [history]="d.history" /></div>
          </article>
        </div>
      } @else {
        <div class="skeleton" style="height: 140px; border-radius: 16px"></div>
        <div class="grid grid-2"><div class="skeleton" style="height: 320px"></div><div class="skeleton" style="height: 320px"></div></div>
      }
    </div>`,
  styles: `
    .back { display: inline-flex; align-items: center; gap: 6px; color: var(--ink-3); font-weight: 600; font-size: 13px; width: fit-content; }
    .back:hover { color: var(--ink); text-decoration: none; }
    .hero { display: flex; gap: 20px; padding: 22px 24px; align-items: center; flex-wrap: wrap;
      background: radial-gradient(90% 140% at 100% 0%, #f4eeff 0%, transparent 55%), var(--surface); }
    .hero-left { flex: 1; min-width: 260px; display: flex; flex-direction: column; gap: 8px; }
    .hero-left h1 { font-size: 22px; }
    .tag.big { font-size: 13px; padding: 4px 9px; }
    .hero-actions { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    .waiting { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--ink-3); background: var(--surface-3); padding: 8px 12px; border-radius: 10px; }
    .layout { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr); gap: var(--s-5); align-items: start; }
    .person { display: flex; align-items: center; gap: 12px; margin-bottom: 18px; }
    .person div { display: flex; flex-direction: column; }
    .person span { font-size: 12.5px; color: var(--ink-3); }
    .comments { white-space: pre-wrap; overflow-wrap: anywhere; background: var(--surface-2); border: 1px solid var(--line); border-radius: 12px; padding: 12px 14px; font-size: 13.5px; color: var(--ink-2); }
    @media (max-width: 960px) { .layout { grid-template-columns: minmax(0, 1fr); } }
  `
})
export class RequestDetailComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly id = input.required<string>();

  protected readonly detail = signal<RequestDetail | null>(null);
  protected readonly error = signal<'missing' | 'failed' | null>(null);
  protected readonly busy = signal<string | null>(null);

  protected readonly actions = computed(() => (this.detail()?.allowedActions ?? []).map(key => ({ key, ...ACTIONS[key] })).filter(a => a.label));
  protected readonly waitingOn = computed(() => {
    switch (this.detail()?.request.status) {
      case 'Submitted': return 'approval';
      case 'Approved': case 'InProgress': return 'IT to process it';
      case 'Delivered': case 'Checking': return 'the employee to confirm';
      case 'Received': return 'IT to close it';
      default: return 'the next step';
    }
  });

  protected readonly initials = initials;
  protected readonly avatarClass = avatarClass;
  protected readonly typeLabel = label;

  constructor() {
    effect(() => this.load(this.id()));
  }

  protected load(id = this.id()): void {
    this.error.set(null);
    this.api.get<RequestDetail>(`requests/${id}`).subscribe({
      next: d => this.detail.set(d),
      error: err => this.error.set(err?.status === 404 ? 'missing' : 'failed')
    });
  }

  protected async run(action: string): Promise<void> {
    const meta = ACTIONS[action];
    const d = this.detail();
    if (!meta || !d || this.busy()) return;

    let note: string | null = null;
    if (action === 'reject') {
      note = await this.notify.prompt(meta.confirm, 'Reason (shared with the requester)', true, 'Reject request');
      if (note === null) return;
    } else {
      const optional = await this.notify.prompt(meta.confirm, 'Note (optional)', false, meta.label);
      if (optional === null) return;
      note = optional || null;
    }

    this.busy.set(action);
    this.api.post<RequestDetail>(`requests/${d.request.id}/${action}`, { note }).subscribe({
      next: updated => {
        this.busy.set(null);
        this.detail.set(updated);
        this.notify.success(meta.success);
      },
      error: err => {
        this.busy.set(null);
        this.notify.error(messageOf(err), 'Could not update the request');
        this.load();
      }
    });
  }

  protected async remove(r: RequestDetail['request']): Promise<void> {
    const ok = await this.notify.confirm({
      title: `Delete ${r.requestNumber}?`,
      text: 'The request and its status history will be removed permanently. This cannot be undone.',
      confirmText: 'Delete permanently',
      danger: true
    });
    if (!ok) return;
    this.api.delete(`requests/${r.id}`).subscribe({
      next: () => {
        this.notify.success(`${r.requestNumber} deleted.`);
        this.router.navigate(['/requests']);
      },
      error: err => this.notify.error(messageOf(err), 'Could not delete the request')
    });
  }
}
