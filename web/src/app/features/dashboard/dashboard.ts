import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, distinctUntilChanged, filter, map, of, startWith, switchMap, tap } from 'rxjs';
import { SvmApi } from '../../core/api/svm-api';
import { DashboardData, Page, VulnerabilityInstance, VulnQuery } from '../../core/models';
import { RiskBadge, StateBlock, StatusBadge } from '../../shared/ui/ui';
import { BarList } from '../../shared/ui/bar-list';
import { SvmIcon } from '../../shared/ui/icon';
import { RISK_COLOR_VAR, RISK_LABEL, SLA_LABEL, STATUS_LABEL } from '../../shared/labels';
import { environment } from '../../../environments/environment';
import { UrgentCard } from './urgent-card';
import { DashboardHelp } from './dashboard-help';

export type UrgentQueue = 'overdue' | 'nearDue' | 'kev';

export function urgentQuery(queue: UrgentQueue): VulnQuery {
  return {
    openOnly: true,
    ...(queue === 'kev' ? { kevOnly: true } : { slaStatus: queue === 'overdue' ? 'Breached' : 'NearDue' }),
    page: 1, pageSize: 3, sort: 'slaDueDate:asc',
  };
}

/** Use the supplied SLA snapshot rather than reinterpreting historic seed data using today's clock. */
export function slaTimeLabel(v: VulnerabilityInstance): string {
  const hours = v.sla.remainingHours;
  if (!Number.isFinite(hours)) return SLA_LABEL[v.sla.breachStatus];
  if (v.sla.breachStatus === 'Met') return SLA_LABEL.Met;
  if (v.sla.breachStatus === 'Breached') {
    return hours < 0 ? `เกิน ${Math.ceil(Math.abs(hours) / 24)} วัน` : SLA_LABEL.Breached;
  }
  return hours > 0 ? `เหลือ ${Math.ceil(hours / 24)} วัน` : SLA_LABEL[v.sla.breachStatus];
}

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe, DecimalPipe, RiskBadge, StateBlock, StatusBadge, BarList, SvmIcon, UrgentCard, DashboardHelp],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.css', './dashboard-responsive.css'],
})
export class Dashboard {
  private readonly api = inject(SvmApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly queueRequests = new Subject<UrgentQueue>();
  private readonly summaryRequests = new Subject<void>();

  protected readonly data = signal<DashboardData | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly canReadInstances = computed(() => this.data()?.canReadInstances !== false);

  protected readonly queue = signal<UrgentQueue>('overdue');
  protected readonly queueData = signal<Page<VulnerabilityInstance> | null>(null);
  protected readonly queueLoading = signal(true);
  protected readonly queueError = signal(false);
  protected readonly isMock = environment.useMock;
  protected readonly riskLabels: Record<string, string> = RISK_LABEL;
  protected readonly riskColors: Record<string, string> = RISK_COLOR_VAR;
  protected readonly statusLabels: Record<string, string> = STATUS_LABEL;
  protected readonly slaLabels: Record<string, string> = SLA_LABEL;
  protected readonly slaColors: Record<string, string> = {
    OnTrack: 'var(--color-risk-low)', NearDue: 'var(--color-risk-medium)',
    Breached: 'var(--color-risk-critical)', Met: 'var(--color-risk-low)',
  };
  protected readonly queueOptions: { key: UrgentQueue; label: string }[] = [
    { key: 'overdue', label: 'เกิน SLA' }, { key: 'nearDue', label: 'ใกล้ครบกำหนด' }, { key: 'kev', label: 'KEV' },
  ];
  protected readonly fullListParams = computed(() => {
    const q = urgentQuery(this.queue());
    return { open: true, ...(q.kevOnly ? { kev: true } : { sla: q.slaStatus }) };
  });
  // The live region stays mounted outside the busy content, including zero-result transitions.
  protected readonly queueResultMessage = computed(() => {
    const result = this.queueData();
    if (this.queueLoading()) return 'กำลังโหลดงานเร่งด่วน';
    if (this.queueError()) return 'โหลดรายการไม่สำเร็จ ลองอีกครั้งเพื่อดูงานในกลุ่มนี้';
    if (!result) return '';
    const label = this.queueOptions.find(option => option.key === this.queue())!.label;
    return result.items.length === 0
      ? `กลุ่ม${label}: ไม่มีงานในกลุ่มนี้`
      : `กลุ่ม${label}: แสดง ${result.items.length} จาก ${result.total.toLocaleString('th-TH')} รายการ`;
  });
  protected readonly riskTotal = computed(() => this.data()?.byRiskLevel.reduce((n, x) => n + x.count, 0) ?? 0);
  protected readonly statusTotal = computed(() => this.data()?.byStatus.reduce((n, x) => n + x.count, 0) ?? 0);
  protected readonly businessUnitTotal = computed(() => this.data()?.byBusinessUnit.reduce((n, x) => n + x.count, 0) ?? 0);
  protected readonly trendMax = computed(() => Math.max(1, ...(this.data()?.trendLast7Days.map(x => x.count) ?? [])));
  protected readonly timeLabel = slaTimeLabel;

  constructor() {
    this.summaryRequests.pipe(
      startWith(undefined),
      tap(() => { this.loading.set(true); this.error.set(false); }),
      switchMap(() => this.api.getDashboard().pipe(
        map(data => ({ data, failed: false })),
        catchError(() => of({ data: null, failed: true })),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(result => {
      this.data.set(result.data);
      this.error.set(result.failed);
      this.loading.set(false);
      if (result.data && this.canReadInstances()) this.queueRequests.next(this.queue());
    });
    this.queueRequests.pipe(
      filter(() => !this.loading() && this.canReadInstances()),
      tap(() => { this.queueLoading.set(true); this.queueError.set(false); this.queueData.set(null); }),
      switchMap(queue => this.api.getVulnerabilities(urgentQuery(queue)).pipe(
        map(data => ({ data, failed: false })),
        catchError(() => of({ data: null, failed: true })),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(result => {
      this.queueData.set(result.data);
      this.queueError.set(result.failed);
      this.queueLoading.set(false);
    });
    this.route.queryParamMap.pipe(
      map((params): UrgentQueue => {
        const queue = params.get('queue');
        return queue === 'nearDue' || queue === 'kev' ? queue : 'overdue';
      }),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(queue => {
      this.queue.set(queue);
      this.queueRequests.next(queue);
    });
  }

  protected selectQueue(queue: UrgentQueue): void {
    if (queue === this.queue()) return;
    void this.router.navigate([], {
      relativeTo: this.route, queryParams: { queue }, queryParamsHandling: 'merge',
    });
  }
  protected retrySummary(): void { this.summaryRequests.next(); }
  protected retryQueue(): void { this.queueRequests.next(this.queue()); }
}
