import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { MockSvmApi } from './mock-svm-api';
import { SlaTimer, VulnStatus, VulnerabilityInstance, VulnQuery } from '../models';

function finding(
  id: string,
  overrides: Partial<Omit<VulnerabilityInstance, 'sla'>> & { sla?: Partial<SlaTimer> } = {},
): VulnerabilityInstance {
  const { sla, ...fields } = overrides;
  return {
    id,
    code: id,
    title: id,
    assetId: `asset-${id}`,
    assetCode: `asset-${id}`,
    assetName: `Asset ${id}`,
    businessUnit: 'Operations',
    location: '/service',
    status: 'New',
    riskScore: 50,
    riskLevel: 'High',
    isKev: false,
    firstDetectedAt: '2026-01-01T00:00:00Z',
    lastDetectedAt: '2026-01-01T00:00:00Z',
    sourceSystem: 'test fixture',
    sla: {
      policyName: 'High-30d',
      startedAt: '2026-01-01T00:00:00Z',
      dueDate: '2026-02-01T00:00:00Z',
      breachStatus: 'OnTrack',
      remainingHours: 100,
      ...sla,
    },
    riskBreakdown: [],
    riskHistory: [],
    statusHistory: [],
    evidence: [],
    comments: [],
    verification: [],
    ...fields,
  };
}

describe('MockSvmApi vulnerability query filters', () => {
  let api: MockSvmApi;
  let http: HttpTestingController;
  let dataLoaded: boolean;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [MockSvmApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(MockSvmApi);
    http = TestBed.inject(HttpTestingController);
    dataLoaded = false;
  });

  afterEach(() => http.verify());

  async function query(query: VulnQuery, rows: VulnerabilityInstance[]) {
    const response = firstValueFrom(api.getVulnerabilities(query));
    if (!dataLoaded) {
      http.expectOne('/mock/vulnerabilities.json').flush(rows);
      dataLoaded = true;
    }
    await new Promise((resolve) => setTimeout(resolve, 275));
    return response;
  }

  it('filters SLA and terminal states before pagination while retaining the filtered total', async () => {
    const rows = [
      finding('closed', { status: 'Closed', riskScore: 100, sla: { breachStatus: 'Breached', dueDate: '2026-01-01' } }),
      finding('wrong-sla', { sla: { breachStatus: 'NearDue', dueDate: '2026-01-02' } }),
      finding('breached-a', { riskScore: 40, sla: { breachStatus: 'Breached', dueDate: '2026-01-03' } }),
      finding('breached-b', { riskScore: 75, sla: { breachStatus: 'Breached', dueDate: '2026-01-04' } }),
    ];

    const result = await query({
      slaStatus: 'Breached', openOnly: true, page: 1, pageSize: 1, sort: 'slaDueDate:asc',
    }, rows);

    expect(result.total).toBe(2);
    expect(result.items.map((item) => item.id)).toEqual(['breached-a']);
    expect(result.page).toBe(1);
    expect(result.pageSize).toBe(1);
  });

  it('excludes every terminal status from open-only results', async () => {
    const rows = (['Closed', 'FalsePositive', 'Rejected'] as VulnStatus[])
      .map((status) => finding(status, { status }));
    rows.push(finding('still-open', { status: 'Verified' }));

    const result = await query({ openOnly: true, page: 1, pageSize: 10 }, rows);

    expect(result.total).toBe(1);
    expect(result.items.map((item) => item.id)).toEqual(['still-open']);
  });

  it('uses the supplied historical SLA status and orders equal due dates by descending risk', async () => {
    const rows = [
      finding('historically-on-track', {
        riskScore: 99,
        sla: { dueDate: '2000-01-01T00:00:00Z', breachStatus: 'OnTrack' },
      }),
      finding('lower-risk', { riskScore: 40, sla: { dueDate: '2026-01-05T00:00:00Z', breachStatus: 'Breached' } }),
      finding('higher-risk', { riskScore: 90, sla: { dueDate: '2026-01-05T00:00:00Z', breachStatus: 'Breached' } }),
      finding('alpha-equal-risk', { riskScore: 90, sla: { dueDate: '2026-01-05T00:00:00Z', breachStatus: 'Breached' } }),
      finding('zeta-equal-risk', { riskScore: 90, sla: { dueDate: '2026-01-05T00:00:00Z', breachStatus: 'Breached' } }),
    ];

    const breached = await query({ slaStatus: 'Breached', sort: 'slaDueDate:asc', pageSize: 10 }, rows);
    expect(breached.items.map((item) => item.id)).toEqual([
      'alpha-equal-risk', 'higher-risk', 'zeta-equal-risk', 'lower-risk',
    ]);
    expect(breached.total).toBe(4);

    const onTrack = await query({ slaStatus: 'OnTrack', pageSize: 10 }, rows);
    expect(onTrack.items.map((item) => item.id)).toEqual(['historically-on-track']);
  });

  it('resolves on a Verified result and reopens on NotResolved', async () => {
    const rows = [
      finding('pass', { status: 'PendingVerification', rowVersion: 'v1' }),
      finding('fail', { status: 'PendingVerification', rowVersion: 'v1' }),
    ];
    const passed = firstValueFrom(api.recordVerification('pass', { rowVersion: 'v1', result: 'Verified', method: 'Manual', notes: 'ok' }));
    http.expectOne('/mock/vulnerabilities.json').flush(rows);
    expect((await passed).toStatus).toBe('Resolved');
    const failed = await firstValueFrom(api.recordVerification('fail', { rowVersion: 'v1', result: 'NotResolved', method: 'Manual', notes: 'no' }));
    expect(failed.toStatus).toBe('Reopened');
  });
});
