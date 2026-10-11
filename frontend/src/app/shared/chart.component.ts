import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, input, viewChild } from '@angular/core';
import { ArcElement, BarController, BarElement, CategoryScale, Chart, ChartConfiguration, DoughnutController, Filler, Legend,
  LineController, LineElement, LinearScale, PointElement, Tooltip } from 'chart.js';

Chart.register(ArcElement, BarController, BarElement, CategoryScale, DoughnutController, Filler, Legend, LineController, LineElement,
  LinearScale, PointElement, Tooltip);

Chart.defaults.font.family = '"Figtree", system-ui, sans-serif';
Chart.defaults.font.size = 12;
Chart.defaults.color = '#6a7090';
Chart.defaults.plugins.tooltip.backgroundColor = '#161a2e';
Chart.defaults.plugins.tooltip.padding = 10;
Chart.defaults.plugins.tooltip.cornerRadius = 10;
Chart.defaults.plugins.tooltip.titleFont = { weight: 600 };
Chart.defaults.plugins.legend.labels.usePointStyle = true;
Chart.defaults.plugins.legend.labels.boxWidth = 8;
Chart.defaults.plugins.legend.labels.boxHeight = 8;

/** Minimal Chart.js host: re-creates the chart whenever the config input changes. */
@Component({
  selector: 'app-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="chart" [style.height.px]="height()"><canvas #canvas [attr.aria-label]="label()" role="img"></canvas></div>`,
  styles: `.chart { position: relative; width: 100%; }`
})
export class ChartComponent implements OnDestroy {
  readonly config = input.required<ChartConfiguration>();
  readonly height = input(260);
  readonly label = input('Chart');

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    effect(() => {
      const config = this.config();
      const el = this.canvas().nativeElement;
      this.chart?.destroy();
      this.chart = new Chart(el, {
        ...config,
        options: { responsive: true, maintainAspectRatio: false, ...config.options }
      });
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
