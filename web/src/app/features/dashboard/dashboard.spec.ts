import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of } from 'rxjs';
import { Dashboard, slaTimeLabel } from './dashboard';
import { SvmApi } from '../../core/api/svm-api';
import { DashboardData, Page, VulnerabilityInstance, VulnQuery } from '../../core/models';

@Component({ template: '' })
class OtherPage {}

const summary: DashboardData = {
  totalOpen: 0, critical: 0, high: 0, slaBreaches: 0, kevOpen: 0, meanTimeToRemediateDays: 0,
  byRiskLevel: [], byStatus: [], byBusinessUnit: [], slaByStatus: [], trendLast7Days: [],
};

function item(code: string): VulnerabilityInstance {
  return {
    id: code, code, title: code, assetId: 'asset', assetCode: 'SRV-01', assetName: 'Server',
    businessUnit: 'IT', location: '/', status: 'New', riskScore: 90, riskLevel: 'Critical', isKev: true,
    firstDetectedAt: '2026-10-01T00:00:00Z', lastDetectedAt: '2026-10-07T00:00:00Z', sourceSystem: 'test',
    sla: { policyName: 'Critical', startedAt: '2026-10-01T00:00:00Z', dueDate: '2026-10-05T00:00:00Z', breachStatus: 'Breached', remainingHours: -58 },
    riskBreakdown: [], riskHistory: [], statusHistory: [], evidence: [], comments: [], verification: [],
  };
}

describe('Dashboard urgent work', () => {
  let requests: { query: VulnQuery; response: Subject<Page<VulnerabilityInstance>> }[];

  beforeEach(async () => {
    requests = [];
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([
        { path: 'dashboard', component: Dashboard },
        { path: 'elsewhere', component: OtherPage },
      ]), { provide: SvmApi, useValue: {
        getDashboard: () => of(summary),
        getVulnerabilities: (query: VulnQuery) => {
          const response = new Subject<Page<VulnerabilityInstance>>();
          requests.push({ query, response });
          return response;
        },
      } }],
    }).compileComponents();
  });

  it('keeps the latest selected queue when an older response arrives late', async () => {
    const harness = await RouterTestingHarness.create('/dashboard');
    const root = harness.routeNativeElement!;
    expect(requests[0].query.slaStatus).toBe('Breached');
    root.querySelectorAll<HTMLButtonElement>('.queue-filter')[2].click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/dashboard?queue=kev');
    expect(requests[1].query).toMatchObject({ kevOnly: true, openOnly: true, pageSize: 3 });
    requests[1].response.next({ items: [item('LATEST')], total: 8, page: 1, pageSize: 3 });
    requests[0].response.next({ items: [item('STALE')], total: 1, page: 1, pageSize: 3 });
    harness.detectChanges();
    expect(root.textContent).toContain('LATEST');
    expect(root.textContent).not.toContain('STALE');
    expect(root.querySelector('.queue-count')?.textContent).toContain('8');
    expect(root.querySelector<HTMLAnchorElement>('.section-head a')?.href).toContain('kev=true');
  });

  it('recovers from a queue error without losing the selected filter', async () => {
    const harness = await RouterTestingHarness.create('/dashboard?queue=nearDue');
    requests[0].response.error(new Error('offline'));
    harness.detectChanges();
    const root = harness.routeNativeElement!;
    expect(root.textContent).toContain('โหลดรายการไม่สำเร็จ');
    root.querySelector<HTMLButtonElement>('.queue-content .recovery button')!.click();
    expect(requests[1].query.slaStatus).toBe('NearDue');
    requests[1].response.next({ items: [], total: 0, page: 1, pageSize: 3 });
    harness.detectChanges();
    expect(root.textContent).toContain('ไม่มีงานในกลุ่มนี้');
  });

  it('restores the selected queue when returning to its URL', async () => {
    const harness = await RouterTestingHarness.create('/dashboard?queue=kev');
    expect(requests[0].query.kevOnly).toBe(true);
    await harness.navigateByUrl('/elsewhere');
    await harness.navigateByUrl('/dashboard?queue=kev');
    expect(requests[1].query.kevOnly).toBe(true);
    const buttons = harness.routeNativeElement!.querySelectorAll<HTMLButtonElement>('.queue-filter');
    expect(buttons[2].getAttribute('aria-pressed')).toBe('true');
  });

  it('falls back to overdue for an unsupported queue in a shared URL', async () => {
    const harness = await RouterTestingHarness.create('/dashboard?queue=invalid');
    expect(requests[0].query.slaStatus).toBe('Breached');
    expect(harness.routeNativeElement!.querySelector('.queue-filter')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('announces loading, failure, empty and populated results through one persistent status region', async () => {
    const harness = await RouterTestingHarness.create('/dashboard');
    const root = harness.routeNativeElement!;
    const status = root.querySelector('.queue-result-status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(status.textContent).toContain('กำลังโหลดงานเร่งด่วน');
    expect(root.querySelectorAll('.urgent-panel [role="status"]').length).toBe(1);
    requests[0].response.error(new Error('offline'));
    harness.detectChanges();
    expect(status.textContent).toContain('โหลดรายการไม่สำเร็จ');
    expect(root.querySelectorAll('.urgent-panel [role="status"]').length).toBe(1);
    root.querySelector<HTMLButtonElement>('.queue-content .recovery button')!.click();
    harness.detectChanges();
    expect(status.textContent).toContain('กำลังโหลดงานเร่งด่วน');
    requests[1].response.next({ items: [], total: 0, page: 1, pageSize: 3 });
    harness.detectChanges();
    expect(status.textContent).toContain('ไม่มีงานในกลุ่มนี้');
    await harness.navigateByUrl('/dashboard?queue=kev');
    expect(root.querySelector('.queue-result-status')).toBe(status);
    requests[2].response.next({ items: [item('NEXT')], total: 1000, page: 1, pageSize: 3 });
    harness.detectChanges();
    expect(status.textContent).toContain('กลุ่มKEV: แสดง 1 จาก 1,000 รายการ');
    expect(root.querySelectorAll('.urgent-panel [role="status"]').length).toBe(1);
  });

  it('identifies each missing summary instead of rendering blank chart sections', async () => {
    const harness = await RouterTestingHarness.create('/dashboard');
    const text = harness.routeNativeElement!.textContent;
    expect(text).toContain('ยังไม่มีข้อมูลสถานะ SLA');
    expect(text).toContain('ยังไม่มีข้อมูลสถานะงาน');
    expect(text).toContain('ยังไม่มีข้อมูลหน่วยธุรกิจ');
  });
  it('uses the supplied SLA snapshot rather than the current system date', () => {
    expect(slaTimeLabel(item('old'))).toBe('เกิน 3 วัน');
    const nearDue = item('near');
    nearDue.sla = { ...nearDue.sla, remainingHours: 20, breachStatus: 'NearDue' };
    expect(slaTimeLabel(nearDue)).toBe('เหลือ 1 วัน');
  });
});
