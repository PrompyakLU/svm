import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Count } from '../../core/models';

/** Text labels and counts remain usable when data is empty or labels are long. */
@Component({
  selector: 'app-bar-list',
  imports: [DecimalPipe],
  template: `
    @if (rows().length === 0) {
      <p class="empty-data">{{ emptyMessage() }}</p>
    } @else {
      <ul class="bars">
        @for (row of rows(); track row.key) {
          <li class="bar">
            <span class="bar__label">{{ row.label }}</span>
            <span class="bar__track" aria-hidden="true">
              <span class="bar__fill" [style.width.%]="row.pct" [style.background]="colorMap()[row.key] || color()"></span>
            </span>
            <span class="bar__value">{{ row.count | number }}</span>
          </li>
        }
      </ul>
    }
  `,
  styles: [`
    .empty-data { margin:0; color:var(--color-fg-secondary); font-size:var(--fs-body-sm); }
    .bars { list-style:none; margin:0; padding:0; display:flex; flex-direction:column; gap:10px; }
    .bar { display:grid; grid-template-columns:minmax(0, 1.1fr) minmax(40px, 1fr) max-content; align-items:center; gap:8px; }
    .bar__label { min-width:0; font-size:var(--fs-label-sm); color:var(--color-fg-primary); overflow-wrap:anywhere; }
    .bar__track { height:10px; background:var(--color-bg-surface-raised); border-radius:var(--radius-full); overflow:hidden; }
    .bar__fill { display:block; height:100%; border-radius:var(--radius-full); transition:width var(--dur-base) var(--ease-standard); }
    .bar__value { font-size:var(--fs-label-sm); font-weight:var(--fw-medium); text-align:end; font-variant-numeric:tabular-nums; }
    @media (prefers-reduced-motion: reduce) { .bar__fill { transition:none; } }
  `],
})
export class BarList {
  readonly data = input.required<Count[]>();
  readonly emptyMessage = input('ยังไม่มีข้อมูล');
  readonly labelMap = input<Record<string, string>>({});
  readonly color = input<string>('var(--color-brand-primary)');
  readonly colorMap = input<Record<string, string>>({});

  protected rows = computed(() => {
    const d = this.data();
    const max = Math.max(1, ...d.map((x) => x.count));
    const map = this.labelMap();
    return d.map((x) => ({ key: x.key, label: map[x.key] ?? x.key, count: x.count, pct: (x.count / max) * 100 }));
  });
}
