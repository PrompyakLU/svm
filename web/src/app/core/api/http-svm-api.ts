import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AdminData, Asset, AuditEntry, Comment, DashboardData, DedupCandidate, Evidence, ImportBatch, NotificationItem, Page, RiskLevel, SlaBreachStatus, VulnerabilityInstance, VulnQuery, VulnStatus, VULN_STATUSES } from '../models';
import { SvmApi } from './svm-api';
import { AuthService } from '../auth/auth.service';
import * as W from './server-contracts';

type Input<K extends keyof SvmApi> = Parameters<SvmApi[K] extends (...args: infer A) => unknown ? (...args: A) => unknown : never>[1];
export interface ServerPage<T> { items: T[]; totalCount?: number; total?: number; totalPages?: number; page: number; pageSize: number; }

function status(value: string): VulnStatus {
  const result = VULN_STATUSES.find(item => item === value);
  if (!result) throw new Error(`Unknown workflow status: ${value}`);
  return result;
}
function risk(value: string | null): RiskLevel {
  switch (value) {
    case null: return 'Informational';
    case 'Critical': case 'High': case 'Medium': case 'Low': case 'Informational': case 'Info': return value;
    default: throw new Error(`Unknown risk level: ${value}`);
  }
}
function slaStatus(value: string): SlaBreachStatus {
  switch (value) {
    case 'OnTrack': case 'NearDue': case 'Breached': case 'Met': case 'NotStarted': case 'Paused': return value;
    default: throw new Error(`Unknown SLA status: ${value}`);
  }
}
export function mapInstance(row: W.InstanceListItemDto): VulnerabilityInstance {
  return {
    id: row.id, code: row.code, title: row.title, assetId: row.assetId, assetCode: row.assetCode,
    assetName: row.assetName, businessUnit: row.businessUnit ?? '', cveId: row.cveId ?? undefined,
    cweId: row.cweId ?? undefined, location: row.location ?? '', port: row.port ?? undefined,
    component: row.component ?? undefined, status: status(row.status), riskScore: row.riskScore ?? 0,
    riskUnscored: row.riskScore === null, riskLevel: risk(row.riskLevel), isKev: row.isKev,
    assignedTo: row.assignedTo ?? undefined, assignedToUserId: row.assignedToUserId ?? undefined,
    firstDetectedAt: row.firstDetectedAt, lastDetectedAt: row.lastDetectedAt, sourceSystem: row.sourceSystem ?? '',
    sla: row.sla ? { ...row.sla, breachStatus: slaStatus(row.sla.breachStatus) } : {
      policyName: 'Not configured', startedAt: '', dueDate: '', breachStatus: 'NotStarted', remainingHours: 0,
    }, riskBreakdown: [], riskHistory: [], statusHistory: [], evidence: [], comments: [], verification: [],
  };
}
export function mapDetail(row: W.InstanceDetailDto): VulnerabilityInstance {
  const instance = mapInstance({ ...row, assetId: row.asset.id, assetCode: row.asset.assetCode,
    assetName: row.asset.name, businessUnit: row.asset.businessUnit, internetExposure: row.asset.internetExposure,
    assetCriticality: row.asset.criticality });
  return { ...instance, rowVersion: row.rowVersion,
    availableTransitions: row.availableTransitions.map(t => ({ ...t, toStatus: status(t.toStatus) })),
    riskBreakdown: row.riskBreakdown,
    riskHistory: row.riskHistory.map(h => ({ ...h, riskLevel: risk(h.riskLevel), reason: h.reason ?? undefined, previousScore: h.previousScore ?? undefined })),
    statusHistory: row.statusHistory.map(h => ({ ...h, fromStatus: h.fromStatus ? status(h.fromStatus) : null, toStatus: status(h.toStatus), reason: h.reason ?? undefined })),
    evidence: row.evidence, comments: row.comments,
    remediation: row.remediation ? { ...row.remediation, assignedTo: row.remediation.assignedTo ?? '', team: row.remediation.team ?? undefined, plan: row.remediation.plan ?? undefined } : undefined,
    verification: row.verification.map(v => ({ ...v, note: v.note ?? undefined })),
  };
}

/** Real HTTP only. HttpErrorResponse retains the server ProblemDetails body for callers. */
@Injectable()
export class HttpSvmApi extends SvmApi {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly base = environment.apiBaseUrl.replace(/\/$/, '');
  private url(path: string): string { return `${this.base}/${path}`; }
  private instance(id: string, action?: string): string { return this.url(`instances/${encodeURIComponent(id)}${action ? '/' + action : ''}`); }
  private pages<T>(path: string, params = new HttpParams(), page = 1, accumulated: T[] = []): Observable<T[]> {
    return this.http.get<ServerPage<T>>(this.url(path), { params: params.set('page', page).set('pageSize', 100) }).pipe(
      switchMap(result => {
        const items = [...accumulated, ...result.items];
        const total = result.totalCount ?? result.total ?? 0;
        return result.items.length > 0 && result.page * result.pageSize < total
          ? this.pages<T>(path, params, result.page + 1, items) : of(items);
      }),
    );
  }
  getOperationsDashboard(): Observable<W.OperationsDashboardDto> { return this.http.get<W.OperationsDashboardDto>(this.url('dashboard/operations')); }
  getExecutiveDashboard(): Observable<W.ExecutiveDashboardDto> { return this.http.get<W.ExecutiveDashboardDto>(this.url('dashboard/executive')); }
  getDashboard(): Observable<DashboardData> {
    const permissions = this.auth.user()?.permissions ?? [];
    const canReadInstances = permissions.includes('instance.read');
    if (permissions.includes('dashboard.ops')) return this.getOperationsDashboard().pipe(map(r => ({
      totalOpen: r.summary.totalOpen, critical: r.summary.criticalOpen, high: r.summary.highOpen,
      slaBreaches: r.summary.overdue, kevOpen: r.summary.kevOpen, meanTimeToRemediateDays: r.meanTimeToRemediateDays,
      byRiskLevel: r.byRiskLevel, byStatus: r.byStatus, byBusinessUnit: r.byBusinessUnit, slaByStatus: r.slaByStatus,
      trendLast7Days: r.trend.map(t => ({ key: t.weekStart, count: t.new })),
      generatedAt: r.generatedAt, organizationWide: r.organizationWide, trendPeriod: 'week', trendMeaning: 'new', canReadInstances,
    })));
    if (permissions.includes('dashboard.exec')) return this.getExecutiveDashboard().pipe(map(r => ({
      totalOpen: r.totals.totalOpen, critical: r.totals.criticalOpen, high: r.totals.highOpen,
      slaBreaches: r.totals.slaBreached, kevOpen: r.totals.kevOpen, meanTimeToRemediateDays: r.totals.meanTimeToRemediateDays,
      byRiskLevel: r.byRiskLevel, byStatus: r.byStatus, byBusinessUnit: r.byBusinessUnit, slaByStatus: r.slaByStatus,
      trendLast7Days: r.trend.map(t => ({ key: t.weekStart, count: t.new })),
      generatedAt: r.generatedAt, organizationWide: r.organizationWide, trendPeriod: 'week', trendMeaning: 'new', canReadInstances,
    })));
    return throwError(() => new HttpErrorResponse({ status: 403, error: { title: 'Forbidden', status: 403, code: 'PERMISSION_DENIED', detail: 'Dashboard permission is required.' } }));
  }
  getVulnerabilities(query: VulnQuery): Observable<Page<VulnerabilityInstance>> {
    let params = new HttpParams();
    for (const key of ['search', 'status', 'riskLevel', 'businessUnit', 'slaStatus', 'page', 'pageSize', 'sort', 'openOnly'] as const) {
      let value = query[key];
      if (key === 'riskLevel' && value === 'Info') value = 'Informational';
      if (key === 'status' && value === 'Verified') value = 'Resolved';
      if (value !== undefined && value !== '') params = params.set(key, String(value));
    }
    if (query.kevOnly !== undefined) params = params.set('isKev', String(query.kevOnly));
    return this.http.get<ServerPage<W.InstanceListItemDto>>(this.url('instances'), { params }).pipe(map(page => ({
      items: page.items.map(mapInstance), total: page.totalCount ?? page.total ?? 0, page: page.page, pageSize: page.pageSize,
    })));
  }
  getVulnerability(id: string): Observable<VulnerabilityInstance | undefined> {
    return this.http.get<W.InstanceDetailDto>(this.instance(id)).pipe(map(mapDetail),
      catchError((error: unknown) => error instanceof HttpErrorResponse && error.status === 404 ? of(undefined) : throwError(() => error)));
  }
  getBusinessUnits(): Observable<string[]> { return this.http.get<{ items: string[] }>(this.url('business-units')).pipe(map(r => r.items)); }
  getAssets(): Observable<Asset[]> {
    return this.pages<W.AssetDto>('assets').pipe(map(rows => rows.map(row => ({ ...row,
      businessUnit: row.businessUnit ?? '', criticality: risk(row.criticality), ipAddress: row.ipAddress ?? undefined, hostname: row.hostname ?? undefined,
    }))));
  }
  getImportBatches(): Observable<ImportBatch[]> { return this.pages<W.ImportBatchDto>('import-batches'); }
  getDedupQueue(): Observable<DedupCandidate[]> {
    return this.pages<W.DedupQueueItemDto>('dedup-queue').pipe(map(rows => rows.map(row => ({
      id: row.findingId, findingId: row.findingId, findingName: row.findingName, assetCode: row.assetCode ?? '',
      cveId: row.cveId ?? undefined, location: row.location ?? '', candidates: row.candidates,
      existingInstanceCode: row.candidates.map(c => c.title).join(', '),
    }))));
  }
  getAudit(search: string): Observable<AuditEntry[]> {
    // Audit has structured filters, not a free-text search parameter. Preserve the existing UI search locally.
    return this.pages<W.AuditRecordDto>('audit').pipe(map(rows => rows.map(row => ({
      id: String(row.id), timestamp: row.timestamp, actor: row.userId ?? 'System', action: row.action,
      objectType: row.objectType, objectId: row.objectId, result: row.result, requestId: '',
    })).filter(row => !search || `${row.actor} ${row.action} ${row.objectType} ${row.objectId} ${row.result}`.toLowerCase().includes(search.toLowerCase()))));
  }
  getNotifications(): Observable<NotificationItem[]> {
    return this.pages<W.NotificationDto>('notifications').pipe(map(rows => rows.map(row => ({
      id: row.id, type: row.type, title: row.subject, body: row.body, createdAt: row.createdAt, read: row.isRead, channel: 'InApp',
    }))));
  }
  markNotificationRead(id: string): Observable<void> { return this.http.post<void>(this.url(`notifications/${encodeURIComponent(id)}/read`), {}); }
  importFindings(file: File, transactionId: string): Observable<W.ImportResultDto> {
    const data = new FormData(); data.append('file', file, file.name); data.append('transactionId', transactionId);
    return this.http.post<W.ImportResultDto>(this.url('findings/import'), data);
  }
  downloadImportTemplate(): Observable<Blob> { return this.http.get(this.url('findings/import-template'), { responseType: 'blob' }); }
  createFinding(input: Parameters<SvmApi['createFinding']>[0]): Observable<W.ManualFindingResultDto> { return this.http.post<W.ManualFindingResultDto>(this.url('findings/manual'), input); }
  resolveDuplicate(findingId: string, input: Input<'resolveDuplicate'>): Observable<W.ResolveDuplicateResultDto> { return this.http.post<W.ResolveDuplicateResultDto>(this.url(`findings/${encodeURIComponent(findingId)}/resolve-duplicate`), input); }
  transitionInstance(id: string, input: Input<'transitionInstance'>): Observable<W.TransitionResultDto> { return this.http.post<W.TransitionResultDto>(this.instance(id, 'transition'), input); }
  assignInstance(id: string, input: Input<'assignInstance'>): Observable<W.AssignResultDto> { return this.http.post<W.AssignResultDto>(this.instance(id, 'assign'), input); }
  getRemediationTask(taskId: string): Observable<W.RemediationTaskDto> { return this.http.get<W.RemediationTaskDto>(this.url(`tasks/${encodeURIComponent(taskId)}`)); }
  updateRemediationTask(taskId: string, input: Input<'updateRemediationTask'>): Observable<W.RemediationTaskDto> { return this.http.put<W.RemediationTaskDto>(this.url(`tasks/${encodeURIComponent(taskId)}`), input); }
  uploadEvidence(id: string, file: File, taskId?: string): Observable<Evidence> {
    const data = new FormData(); data.append('file', file, file.name); if (taskId) data.append('taskId', taskId);
    return this.http.post<W.EvidenceDto>(this.instance(id, 'evidence'), data).pipe(map(r => ({ id: r.id, fileName: r.fileName,
      type: r.contentType, version: r.version, uploadedBy: r.uploadedBy, uploadedAt: r.uploadedAt, scanResult: r.scanStatus })));
  }
  addComment(id: string, body: string): Observable<Comment> {
    return this.http.post<W.CommentDto>(this.instance(id, 'comments'), { body }).pipe(map(r => ({ id: r.id, body: r.body, author: r.createdBy, createdAt: r.createdAt })));
  }
  recalculateRisk(id: string, _reason: string): Observable<W.InstanceRiskDto> { return this.http.post<W.InstanceRiskDto>(this.instance(id, 'recalculate'), {}); }
  adjustRisk(id: string, input: Input<'adjustRisk'>): Observable<W.InstanceRiskDto> { return this.http.put<W.InstanceRiskDto>(this.instance(id, 'risk'), input); }
  submitVerification(id: string, input: Input<'submitVerification'>): Observable<W.TransitionResultDto> { return this.http.post<W.TransitionResultDto>(this.instance(id, 'submit-verification'), input); }
  rescanInstance(id: string): Observable<W.RescanResultDto> { return this.http.post<W.RescanResultDto>(this.instance(id, 'rescan'), {}); }
  recordVerification(id: string, input: Input<'recordVerification'>): Observable<W.VerificationRecordedDto> { return this.http.post<W.VerificationRecordedDto>(this.instance(id, 'verification'), input); }
  getMasterDataTypes(): Observable<W.MasterDataTypeDto[]> { return this.http.get<W.MasterDataTypeDto[]>(this.url('admin/master-data')); }
  getMasterData(type: string): Observable<W.MasterDataItemDto[]> { return this.http.get<W.MasterDataItemDto[]>(this.url(`admin/master-data/${encodeURIComponent(type)}`)); }
  saveMasterData(type: string, input: Input<'saveMasterData'>, id?: string): Observable<W.MasterDataItemDto> { return this.saveConfig(`master-data/${encodeURIComponent(type)}`, input, id); }
  getStatusTransitions(): Observable<W.StatusTransitionDto[]> { return this.http.get<W.StatusTransitionDto[]>(this.url('admin/status-transitions')); }
  saveStatusTransition(input: Parameters<SvmApi['saveStatusTransition']>[0], id?: string): Observable<W.StatusTransitionDto> { return this.saveConfig('status-transitions', input, id); }
  getSlaPolicies(): Observable<W.SlaPolicyDto[]> { return this.http.get<W.SlaPolicyDto[]>(this.url('admin/sla-policies')); }
  saveSlaPolicy(input: Parameters<SvmApi['saveSlaPolicy']>[0], id?: string): Observable<W.SlaPolicyDto> { return this.saveConfig('sla-policies', input, id); }
  getAssignmentRules(): Observable<W.AssignmentRuleDto[]> { return this.http.get<W.AssignmentRuleDto[]>(this.url('admin/assignment-rules')); }
  saveAssignmentRule(input: Parameters<SvmApi['saveAssignmentRule']>[0], id?: string): Observable<W.AssignmentRuleDto> { return this.saveConfig('assignment-rules', input, id); }
  private saveConfig<T>(path: string, input: unknown, id?: string): Observable<T> {
    return id ? this.http.put<T>(this.url(`admin/${path}/${encodeURIComponent(id)}`), input) : this.http.post<T>(this.url(`admin/${path}`), input);
  }
  getRiskWeights(): Observable<W.RiskWeightsDto> { return this.http.get<W.RiskWeightsDto>(this.url('admin/risk-weights')); }
  saveRiskWeights(input: W.ReplaceRiskWeightsRequest): Observable<W.RiskWeightsDto> { return this.http.put<W.RiskWeightsDto>(this.url('admin/risk-weights'), input); }
  validateRiskWeights(input: W.ReplaceRiskWeightsRequest): Observable<W.RiskWeightsValidationDto> { return this.http.post<W.RiskWeightsValidationDto>(this.url('admin/risk-weights/validate'), input); }
  getRiskThresholds(): Observable<W.RiskThresholdsDto> { return this.http.get<W.RiskThresholdsDto>(this.url('admin/risk-thresholds')); }
  saveRiskThresholds(input: W.ReplaceRiskThresholdsRequest): Observable<W.RiskThresholdsDto> { return this.http.put<W.RiskThresholdsDto>(this.url('admin/risk-thresholds'), input); }
  getAdminData(): Observable<AdminData> {
    return forkJoin({ weights: this.getRiskWeights(), thresholds: this.getRiskThresholds(), sla: this.getSlaPolicies(), transitions: this.getStatusTransitions(), rules: this.getAssignmentRules() }).pipe(map(r => ({
      riskWeights: r.weights.weights.map(w => ({ ...w, id: w.factor })),
      riskThresholds: r.thresholds.thresholds.map(t => ({ ...t, riskLevel: risk(t.riskLevel) })),
      slaPolicies: r.sla.map(p => ({ ...p, riskLevel: risk(p.riskLevel), remediationDays: p.resolveHours / 24, verificationDays: 0 })),
      statusTransitions: r.transitions.map(t => ({ ...t, fromStatus: status(t.fromStatus), toStatus: status(t.toStatus), requiredPermission: t.requiredPermission ?? undefined })),
      assignmentRules: r.rules.map(a => ({ ...a, assignTo: a.assignToUserId ?? a.assignToTeam ?? '' })), users: [],
    })));
  }
}
