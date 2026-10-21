import { Component, input } from '@angular/core';
import { SvmIcon } from './icon';
import { RiskLevel, SlaBreachStatus, VulnStatus } from '../../core/models';
import { RISK_COLOR_VAR, RISK_LABEL, SLA_LABEL, STATUS_LABEL, slaColorVar } from '../labels';

/** ป้ายระดับความเสี่ยง: จุดสี + ข้อความ (สื่อด้วยสี+รูปทรง+label — WCAG AA) */
@Component({
  selector: 'app-risk-badge',
  imports: [SvmIcon],
  template: `<span class="badge" [style.color]="color()" [style.borderColor]="color()">
    <app-icon [name]="level() === 'Critical' || level() === 'High' ? 'alert' : 'shield'" [size]="13" />{{ label() }}
  </span>`,
  styles: [`
    .badge { display:inline-flex; align-items:center; gap:6px; border:1px solid; border-radius:var(--radius-sm);
      background:color-mix(in srgb,currentColor 7%,transparent);
      padding:3px 8px; font-size:var(--fs-label-sm); font-weight:var(--fw-medium); white-space:nowrap; }
    .dot { width:8px; height:8px; border-radius:var(--radius-full); flex:none; }
  `],
})
export class RiskBadge {
  readonly level = input.required<RiskLevel>();
  protected color() { return RISK_COLOR_VAR[this.level()]; }
  protected label() { return RISK_LABEL[this.level()]; }
}

/** ป้ายสถานะ workflow (โทนกลาง) */
@Component({
  selector: 'app-status-badge',
  template: `<span class="st">{{ label() }}</span>`,
  styles: [`
    .st { display:inline-flex; align-items:center; border:1px solid var(--color-bg-border);
      background:var(--color-risk-info-soft); color:var(--color-risk-info);
      border-radius:var(--radius-sm); padding:3px 8px; font-size:var(--fs-label-sm); white-space:nowrap; }
  `],
})
export class StatusBadge {
  readonly status = input.required<VulnStatus>();
  protected label() { return STATUS_LABEL[this.status()]; }
}

/** ป้ายสถานะ SLA */
@Component({
  selector: 'app-sla-badge',
  template: `<span class="badge" [style.color]="color()" [style.borderColor]="color()">
    <span class="dot" [style.background]="color()" aria-hidden="true"></span>{{ label() }}
  </span>`,
  styles: [`
    .badge { display:inline-flex; align-items:center; gap:6px; border:1px solid; border-radius:var(--radius-full);
      padding:2px 10px; font-size:var(--fs-label-sm); font-weight:var(--fw-medium); white-space:nowrap; }
    .dot { width:8px; height:8px; border-radius:var(--radius-full); flex:none; }
  `],
})
export class SlaBadge {
  readonly status = input.required<SlaBreachStatus>();
  protected color() { return slaColorVar(this.status()); }
  protected label() { return SLA_LABEL[this.status()]; }
}

/** การ์ดตัวเลขสรุป (KPI) */
@Component({
  selector: 'app-stat-card',
  template: `<div class="card">
    <div class="card__label">{{ label() }}</div>
    <div class="card__value" [style.color]="accent() || null">{{ value() }}</div>
    @if (hint()) { <div class="card__hint">{{ hint() }}</div> }
  </div>`,
  styles: [`
    .card { border:1px solid var(--color-bg-border); border-radius:var(--radius-md);
      background:var(--color-bg-surface); padding:16px; display:flex; flex-direction:column; gap:4px;
      box-shadow:var(--shadow-sm); }
    .card__label { font-size:var(--fs-label-sm); color:var(--color-fg-secondary); }
    .card__value { font-size:28px; line-height:1.1; font-weight:var(--fw-bold); color:var(--color-fg-primary); }
    .card__hint { font-size:var(--fs-caption); color:var(--color-fg-secondary); }
  `],
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly hint = input<string>('');
  readonly accent = input<string>('');
}

/** สถานะ loading / empty / error / skeleton ของหน้า */
@Component({
  selector: 'app-state-block',
  template: `<div class="sb" [class.sb--skeleton]="mode() === 'skeleton'"
      [attr.role]="announce() ? 'status' : null"
      [attr.aria-busy]="mode() === 'loading' || mode() === 'skeleton'">
    @if (mode() === 'loading') {
      <div class="sb__spinner" aria-hidden="true"></div>
      <p>{{ message() || 'กำลังโหลด...' }}</p>
    } @else if (mode() === 'skeleton') {
      <span class="visually-hidden">{{ message() || 'กำลังโหลด...' }}</span>
      <div class="sk" aria-hidden="true">
        @for (row of skeletonRows(); track $index) { <div class="skeleton sk__row"></div> }
      </div>
    } @else if (mode() === 'empty') {
      <svg width="40" height="40" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>
      <p>{{ message() || 'ไม่มีข้อมูล' }}</p>
    } @else {
      <svg width="40" height="40" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 8v5M12 16h.01M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
      <p>{{ message() || 'เกิดข้อผิดพลาดในการโหลดข้อมูล' }}</p>
    }
  </div>`,
  styles: [`
    .sb { display:flex; flex-direction:column; align-items:center; justify-content:center; gap:12px;
      padding:48px 16px; color:var(--color-fg-secondary); text-align:center; }
    .sb--skeleton { padding:16px 0; align-items:stretch; }
    .sb__spinner { width:28px; height:28px; border:3px solid var(--color-bg-border);
      border-top-color:var(--color-brand-primary); border-radius:var(--radius-full); animation:spin 0.8s linear infinite; }
    .sk { display:flex; flex-direction:column; gap:10px; width:100%; }
    .sk__row { height:20px; }
    .sk__row:first-child { height:28px; width:40%; }
    .sk__row:nth-child(even) { width:92%; }
    .sk__row:nth-child(odd) { width:100%; }
    @keyframes spin { to { transform:rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .sb__spinner { animation-duration:0.01ms; } }
  `],
})
export class StateBlock {
  readonly announce = input(true);
  readonly mode = input.required<'loading' | 'skeleton' | 'empty' | 'error'>();
  readonly message = input<string>('');
  readonly rows = input<number>(5);
  protected skeletonRows() { return Array.from({ length: Math.max(1, this.rows()) }); }
}
