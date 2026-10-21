import { Component, input } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { VulnerabilityInstance } from '../../core/models';
import { RiskBadge, StatusBadge } from '../../shared/ui/ui';
import { SvmIcon } from '../../shared/ui/icon';

/** Narrow-screen task row keeps the primary action beside the information needed to choose it. */
@Component({
  selector: 'app-urgent-card',
  imports: [DatePipe, DecimalPipe, RouterLink, RiskBadge, StatusBadge, SvmIcon],
  template: `
    <article>
      <h3><a [routerLink]="['/vulnerabilities', vulnerability().id]">{{ vulnerability().title }}</a></h3>
      <p class="metadata">{{ vulnerability().code }} · {{ vulnerability().assetCode }}
        @if (vulnerability().isKev) { <span class="kev-tag">KEV</span> }
      </p>
      <div class="badges"><app-risk-badge [level]="vulnerability().riskLevel" />
        <app-status-badge [status]="vulnerability().status" />
        <span class="metadata">คะแนน {{ vulnerability().riskScore | number:'1.1-1' }}</span>
      </div>
      <dl><div><dt>กำหนดแก้ไข</dt><dd><time [attr.datetime]="vulnerability().sla.dueDate">{{ vulnerability().sla.dueDate | date:'dd MMM yyyy' }}</time>
        <span [class.overdue]="vulnerability().sla.breachStatus === 'Breached'">{{ timeLabel() }}</span></dd></div>
        <div><dt>ผู้รับผิดชอบ</dt><dd>{{ vulnerability().assignedTo || 'ยังไม่มอบหมาย' }}</dd></div>
      </dl>
      <a class="btn-outline open-link" [routerLink]="['/vulnerabilities', vulnerability().id]" [attr.aria-label]="'เปิดงาน ' + vulnerability().code">เปิดงาน <app-icon name="arrow-right" [size]="16" /></a>
    </article>
  `,
  styles: [`
    :host{display:block}
    article{padding-block:var(--space-md);border-bottom:1px solid var(--color-bg-border)}
    h3{margin:0;font-size:var(--fs-body-lg);font-weight:var(--fw-medium);overflow-wrap:anywhere}
    h3 a{color:var(--color-fg-primary);text-decoration:none}
    h3 a:hover{text-decoration:underline;color:var(--color-brand-primary)}
    .metadata{margin:var(--space-xs) 0;color:var(--color-fg-secondary);font-size:var(--fs-body-sm);overflow-wrap:anywhere}
    .badges{display:flex;flex-wrap:wrap;align-items:center;gap:var(--space-sm);margin-block:var(--space-sm)}
    dl{display:grid;grid-template-columns:1fr 1fr;gap:var(--space-sm);margin:var(--space-sm) 0 var(--space-md)}
    dt{color:var(--color-fg-secondary);font-size:var(--fs-body-sm)}
    dd{margin:var(--space-xs) 0 0;font-size:var(--fs-body);overflow-wrap:anywhere}
    dd span{display:block;color:var(--color-fg-secondary)}
    dd .overdue{color:var(--color-risk-critical);font-weight:var(--fw-medium)}
    .open-link{width:100%;min-height:44px}
  `],
})
export class UrgentCard {
  readonly vulnerability = input.required<VulnerabilityInstance>();
  readonly timeLabel = input.required<string>();
}
