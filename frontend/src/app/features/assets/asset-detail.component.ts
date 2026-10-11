import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { AssetDetail, AssetHistoryItem } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { avatarClass, initials } from '../../core/labels';
import { IconComponent } from '../../shared/icon.component';
import { HistoryTimelineComponent } from '../../shared/timeline.component';
import { BadgeComponent, EmptyStateComponent, ErrorStateComponent, LocalDatePipe, MoneyPipe } from '../../shared/ui';
import { AssetActionMode, AssetActionsComponent } from './asset-actions.component';
import { AssetFormComponent } from './asset-form.component';

@Component({
  selector: 'app-asset-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, HistoryTimelineComponent, BadgeComponent, EmptyStateComponent, ErrorStateComponent, LocalDatePipe, MoneyPipe,
    AssetActionsComponent, AssetFormComponent],
  templateUrl: './asset-detail.component.html',
  styleUrl: './asset-detail.component.css'
})
export class AssetDetailComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  protected readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  private readonly actions = viewChild.required<AssetActionsComponent>('actions');

  protected readonly detail = signal<AssetDetail | null>(null);
  protected readonly history = signal<AssetHistoryItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<'missing' | 'failed' | null>(null);
  protected readonly tab = signal<'history' | 'assignments' | 'components'>('history');
  protected readonly editOpen = signal(false);
  protected readonly deleting = signal(false);

  protected readonly asset = computed(() => this.detail()?.asset ?? null);
  protected readonly canManage = computed(() => this.auth.can('Assets.View'));
  protected readonly warrantyState = computed(() => {
    const w = this.asset()?.warrantyExpiry;
    if (!w) return null;
    const days = Math.ceil((new Date(`${w}T00:00:00`).getTime() - Date.now()) / 86400000);
    if (days < 0) return { tone: 'danger', text: 'Warranty expired' };
    if (days <= 30) return { tone: 'warning', text: `Warranty ends in ${days} day${days === 1 ? '' : 's'}` };
    return { tone: 'success', text: 'Under warranty' };
  });

  /** Lifecycle buttons available for the current status and the user's permissions. */
  protected readonly available = computed(() => {
    const a = this.asset();
    if (!a) return [];
    const can = (p: string) => this.auth.can(p);
    const list: { mode: AssetActionMode; label: string; icon: string; primary?: boolean; danger?: boolean }[] = [];
    if (a.status === 'InStock' && can('Assets.Assign')) list.push({ mode: 'assign', label: 'Assign', icon: 'user-check', primary: true });
    if (a.status === 'Assigned' && can('Assets.Return')) list.push({ mode: 'return', label: 'Return', icon: 'undo', primary: true });
    if (['InStock', 'Assigned', 'Damaged'].includes(a.status) && can('Assets.Repair')) list.push({ mode: 'repair-start', label: 'Send to repair', icon: 'wrench' });
    if (a.status === 'UnderRepair' && can('Assets.Repair')) list.push({ mode: 'repair-complete', label: 'Complete repair', icon: 'check-circle', primary: true });
    if (!['Scrapped', 'Retired'].includes(a.status) && can('Assets.Repair')) list.push({ mode: 'replace', label: 'Replace component', icon: 'cpu' });
    if (a.status === 'InStock' && can('Assets.Edit')) list.push({ mode: 'damaged', label: 'Mark damaged', icon: 'alert' });
    if (a.status === 'Damaged' && can('Assets.Edit')) list.push({ mode: 'release', label: 'Release to stock', icon: 'package-plus' });
    if (['InStock', 'Damaged'].includes(a.status) && can('Assets.Edit')) list.push({ mode: 'retire', label: 'Retire', icon: 'archive' });
    if (!['Scrapped', 'Assigned'].includes(a.status) && can('Assets.Scrap')) list.push({ mode: 'scrap', label: 'Scrap', icon: 'trash', danger: true });
    return list;
  });

  protected readonly canReplace = computed(() => this.available().some(x => x.mode === 'replace'));

  protected readonly initials = initials;
  protected readonly avatarClass = avatarClass;

  constructor() {
    effect(() => {
      // A different asset starts on its history again, instead of inheriting the previous asset's tab.
      this.tab.set('history');
      this.load(this.id());
    });
  }

  protected load(id = this.id()): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      detail: this.api.get<AssetDetail>(`assets/${id}`),
      history: this.api.get<AssetHistoryItem[]>(`assets/${id}/history`)
    }).subscribe({
      next: ({ detail, history }) => {
        this.detail.set(detail);
        this.history.set(history);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err?.status === 404 ? 'missing' : 'failed');
      }
    });
  }

  protected run(mode: AssetActionMode): void {
    this.actions().start(mode);
  }

  protected onSaved(): void {
    this.editOpen.set(false);
    this.load();
  }

  protected async remove(): Promise<void> {
    const a = this.asset();
    if (!a || this.deleting()) return;
    const ok = await this.notify.confirm({
      title: `Delete ${a.assetTag}?`,
      text: `${a.name} and its history will be removed permanently. This cannot be undone.`,
      confirmText: 'Delete permanently',
      danger: true
    });
    if (!ok) return;
    this.deleting.set(true);
    this.api.delete(`assets/${a.id}`).subscribe({
      next: () => {
        this.notify.success(`${a.assetTag} deleted.`);
        this.router.navigate(['/assets']);
      },
      error: err => {
        this.deleting.set(false);
        this.notify.apiError(err, 'Could not delete the asset');
      }
    });
  }
}
