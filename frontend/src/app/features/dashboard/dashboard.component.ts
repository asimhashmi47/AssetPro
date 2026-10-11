import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Dashboard } from '../../core/models';
import { label } from '../../core/labels';
import { ChartComponent } from '../../shared/chart.component';
import { IconComponent } from '../../shared/icon.component';
import { HistoryTimelineComponent } from '../../shared/timeline.component';
import { BadgeComponent, EmptyStateComponent, ErrorStateComponent, MoneyPipe, PageHeaderComponent, StatCardComponent } from '../../shared/ui';

const STATUS_COLORS: Record<string, string> = {
  InStock: '#10b981', Assigned: '#6366f1', UnderRepair: '#f59e0b', Damaged: '#ef4444', Scrapped: '#94a3b8', Retired: '#cbd5e1'
};
const TYPE_COLORS = ['#6366f1', '#8b5cf6', '#06b6d4', '#10b981', '#f59e0b', '#ec4899', '#0ea5e9', '#14b8a6', '#f97316', '#a855f7'];

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ChartComponent, IconComponent, HistoryTimelineComponent, BadgeComponent, EmptyStateComponent, ErrorStateComponent,
    MoneyPipe, PageHeaderComponent, StatCardComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css'
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);

  protected readonly data = signal<Dashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly today = new Date().toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' });

  protected readonly utilisation = computed(() => {
    const d = this.data();
    if (!d) return 0;
    const usable = d.assignedAssets + d.availableAssets;
    return usable === 0 ? 0 : Math.round((d.assignedAssets / usable) * 100);
  });

  protected readonly statusChart = computed<ChartConfiguration | null>(() => {
    const d = this.data();
    if (!d) return null;
    const rows = d.statusBreakdown.filter(s => s.name !== 'Retired');
    return {
      type: 'doughnut',
      data: {
        labels: rows.map(r => label(r.name)),
        datasets: [{ data: rows.map(r => r.count), backgroundColor: rows.map(r => STATUS_COLORS[r.name]), borderWidth: 3, borderColor: '#fff', hoverOffset: 6 }]
      },
      options: { cutout: '70%', plugins: { legend: { position: 'bottom', labels: { padding: 14 } } } }
    };
  });

  protected readonly typeChart = computed<ChartConfiguration | null>(() => {
    const d = this.data();
    if (!d) return null;
    return {
      type: 'bar',
      data: {
        labels: d.typeBreakdown.map(t => t.name),
        datasets: [{ label: 'Assets', data: d.typeBreakdown.map(t => t.count), backgroundColor: d.typeBreakdown.map((_, i) => TYPE_COLORS[i % TYPE_COLORS.length]),
          borderRadius: 8, borderSkipped: false, maxBarThickness: 34 }]
      },
      options: {
        plugins: { legend: { display: false } },
        scales: { x: { grid: { display: false } }, y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: '#eef0f6' }, border: { display: false } } }
      }
    };
  });

  protected readonly requestChart = computed<ChartConfiguration | null>(() => {
    const d = this.data();
    if (!d) return null;
    return {
      type: 'line',
      data: {
        labels: d.requestTrend.map(p => p.month),
        datasets: [
          { label: 'Submitted', data: d.requestTrend.map(p => p.primary), borderColor: '#8b5cf6', backgroundColor: 'rgba(139, 92, 246, .12)', fill: true, tension: .38, pointRadius: 3, pointBackgroundColor: '#8b5cf6' },
          { label: 'Completed', data: d.requestTrend.map(p => p.secondary), borderColor: '#10b981', backgroundColor: 'rgba(16, 185, 129, .06)', fill: true, tension: .38, pointRadius: 3, pointBackgroundColor: '#10b981' }
        ]
      },
      options: {
        interaction: { mode: 'index', intersect: false },
        plugins: { legend: { position: 'top', align: 'end' } },
        scales: { x: { grid: { display: false } }, y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: '#eef0f6' }, border: { display: false } } }
      }
    };
  });

  protected readonly assignmentChart = computed<ChartConfiguration | null>(() => {
    const d = this.data();
    if (!d) return null;
    return {
      type: 'bar',
      data: {
        labels: d.assignmentTrend.map(p => p.month),
        datasets: [
          { label: 'Assigned', data: d.assignmentTrend.map(p => p.primary), backgroundColor: '#6366f1', borderRadius: 6, maxBarThickness: 18 },
          { label: 'Returned', data: d.assignmentTrend.map(p => p.secondary), backgroundColor: '#06b6d4', borderRadius: 6, maxBarThickness: 18 }
        ]
      },
      options: {
        interaction: { mode: 'index', intersect: false },
        plugins: { legend: { position: 'top', align: 'end' } },
        scales: { x: { grid: { display: false } }, y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: '#eef0f6' }, border: { display: false } } }
      }
    };
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api.get<Dashboard>('dashboard').subscribe({
      next: d => { this.data.set(d); this.loading.set(false); },
      error: () => { this.error.set(true); this.loading.set(false); }
    });
  }
}
