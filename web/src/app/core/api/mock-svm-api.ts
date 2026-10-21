import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, delay, map, of, shareReplay, switchMap, defer, from } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AdminData, Asset, AuditEntry, DashboardData, DedupCandidate, ImportBatch,
  NotificationItem, Page, VulnerabilityInstance, VulnQuery, Evidence, Comment, VulnStatus, RiskLevel,
} from '../models';
import { SvmApi } from './svm-api';
import * as W from './server-contracts';
type Input<K extends keyof SvmApi> = Parameters<SvmApi[K] extends (...args: infer A) => unknown ? (...args: A) => unknown : never>[1];

/** หน่วงเวลาเล็กน้อยให้เหมือนเรียก API จริง (เห็น loading state) */
const LATENCY = 250;

@Injectable()
export class MockSvmApi extends SvmApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.mockBaseUrl;

  private vulns$?: Observable<VulnerabilityInstance[]>;

  private loadVulns(): Observable<VulnerabilityInstance[]> {
    this.vulns$ ??= this.http
      .get<VulnerabilityInstance[]>(`${this.base}/vulnerabilities.json`)
      .pipe(map(rows => rows.map(row => ({ ...row, rowVersion: row.rowVersion ?? 'demo-1', availableTransitions: row.availableTransitions ?? [] }))), shareReplay(1));
    return this.vulns$;
  }

  getDashboard(): Observable<DashboardData> {
    return this.http.get<DashboardData>(`${this.base}/dashboard.json`).pipe(delay(LATENCY));
  }

  getVulnerabilities(query: VulnQuery): Observable<Page<VulnerabilityInstance>> {
    const page = query.page ?? 1;
    const pageSize = query.pageSize ?? 10;
    return this.loadVulns().pipe(
      delay(LATENCY),
      map((all) => {
        let items = all.filter((v) => {
          if (query.status && v.status !== query.status) return false;
          if (query.riskLevel && v.riskLevel !== query.riskLevel) return false;
          if (query.businessUnit && v.businessUnit !== query.businessUnit) return false;
          if (query.kevOnly && !v.isKev) return false;
          if (query.slaStatus && v.sla.breachStatus !== query.slaStatus) return false;
          if (query.openOnly && ['Closed', 'FalsePositive', 'Rejected', 'Resolved', 'RiskAccepted', 'ExceptionApproved', 'Archived'].includes(v.status)) return false;
          if (query.search) {
            const q = query.search.toLowerCase();
            const hay = `${v.code} ${v.title} ${v.assetCode} ${v.assetName} ${v.cveId ?? ''} ${v.location}`.toLowerCase();
            if (!hay.includes(q)) return false;
          }
          return true;
        });

        items = this.sortItems(items, query.sort);
        const total = items.length;
        const start = (page - 1) * pageSize;
        return { items: items.slice(start, start + pageSize), total, page, pageSize };
      }),
    );
  }

  private sortItems(items: VulnerabilityInstance[], sort?: string): VulnerabilityInstance[] {
    const [field, dir] = (sort ?? 'riskScore:desc').split(':');
    const sign = dir === 'asc' ? 1 : -1;
    return [...items].sort((a, b) => {
      switch (field) {
        case 'riskScore': return (a.riskScore - b.riskScore) * sign;
        case 'lastDetectedAt': return (Date.parse(a.lastDetectedAt) - Date.parse(b.lastDetectedAt)) * sign;
        case 'slaDueDate': {
          const dueDateOrder = (Date.parse(a.sla.dueDate) - Date.parse(b.sla.dueDate)) * sign;
          return dueDateOrder || (b.riskScore - a.riskScore) || a.id.localeCompare(b.id);
        }
        case 'title': return a.title.localeCompare(b.title) * sign;
        case 'status': return a.status.localeCompare(b.status) * sign;
        default: return (a.riskScore - b.riskScore) * sign;
      }
    });
  }

  getVulnerability(id: string): Observable<VulnerabilityInstance | undefined> {
    return this.loadVulns().pipe(
      delay(LATENCY),
      map((all) => all.find((v) => v.id === id || v.code === id)),
    );
  }

  getBusinessUnits(): Observable<string[]> {
    return this.loadVulns().pipe(
      map((all) => [...new Set(all.map((v) => v.businessUnit))].sort()),
    );
  }

  getAssets(): Observable<Asset[]> {
    return this.http.get<Asset[]>(`${this.base}/assets.json`).pipe(delay(LATENCY));
  }

  getImportBatches(): Observable<ImportBatch[]> {
    return this.batches$ ??= this.http.get<ImportBatch[]>(`${this.base}/import-batches.json`).pipe(shareReplay(1), delay(LATENCY));
  }

  getDedupQueue(): Observable<DedupCandidate[]> {
    return this.dedup$ ??= this.http.get<DedupCandidate[]>(`${this.base}/dedup-queue.json`).pipe(map(rows => rows.map(row => ({ ...row, findingId: row.findingId ?? row.id }))), shareReplay(1), delay(LATENCY));
  }

  getAudit(search: string): Observable<AuditEntry[]> {
    return this.http.get<AuditEntry[]>(`${this.base}/audit.json`).pipe(
      delay(LATENCY),
      map((all) => {
        if (!search) return all;
        const q = search.toLowerCase();
        return all.filter((a) =>
          `${a.actor} ${a.action} ${a.objectType} ${a.objectId} ${a.result}`.toLowerCase().includes(q),
        );
      }),
    );
  }

  getNotifications(): Observable<NotificationItem[]> {
    return this.notifications$ ??= this.http.get<NotificationItem[]>(`${this.base}/notifications.json`).pipe(shareReplay(1), delay(LATENCY));
  }

  getAdminData(): Observable<AdminData> {
    return this.admin$ ??= this.http.get<AdminData>(`${this.base}/admin.json`).pipe(shareReplay(1), delay(LATENCY));
  }

  private batches$?: Observable<ImportBatch[]>;
  private dedup$?: Observable<DedupCandidate[]>;
  private notifications$?: Observable<NotificationItem[]>;
  private admin$?: Observable<AdminData>;
  private readonly tasks = new Map<string, W.RemediationTaskDto>();
  private readonly imports = new Map<string, { content: string; result: W.ImportResultDto }>();
  private revision = 1;
  private now(): string { return new Date().toISOString(); }
  private version(): string { return 'demo-' + ++this.revision; }
  private fail(code: string, detail: string, status = 422): never {
    throw new HttpErrorResponse({ status, error: { title: 'Demo request failed', status, code, detail }, statusText: detail });
  }
  private mutate<T>(id: string, apply: (row: VulnerabilityInstance) => T): Observable<T> {
    return this.loadVulns().pipe(map(rows => {
      const row = rows.find(row => row.id === id || row.code === id);
      if (!row) return this.fail('INSTANCE_NOT_FOUND', 'Instance not found.', 404);
      return apply(row);
    }));
  }
  private checkVersion(row: { rowVersion?: string }, version?: string | null): void {
    if (!version) this.fail('ROW_VERSION_REQUIRED', 'Reload the record before saving.', 400);
    if (row.rowVersion !== version) this.fail('CONCURRENCY_CONFLICT', 'The record has changed. Reload and try again.', 409);
  }
  private transition(row: VulnerabilityInstance, toStatus: string, reason?: string | null): W.TransitionResultDto {
    const target = this.demoStatus(toStatus);
    const fromStatus = row.status;
    const historyId = crypto.randomUUID(), changedAt = this.now();
    row.status = target; row.rowVersion = this.version();
    row.statusHistory.push({ id: historyId, fromStatus, toStatus: target, reason: reason ?? undefined, changedBy: 'Demo operator', changedAt });
    return { instanceId: row.id, fromStatus, toStatus: target, historyId, changedAt, rowVersion: row.rowVersion };
  }
  private demoStatus(value: string): VulnStatus {
    switch (value) {
      case 'New': case 'UnderReview': case 'Confirmed': case 'Assigned': case 'InProgress': case 'PendingVerification': case 'Resolved': case 'Closed': case 'Reopened': case 'FalsePositive': case 'RiskAccepted': case 'ExceptionApproved': case 'Archived': return value;
      default: return this.fail('INVALID_STATUS', 'Unknown workflow status.', 400);
    }
  }
  private demoRisk(value: string): RiskLevel {
    switch (value) {
      case 'Critical': case 'High': case 'Medium': case 'Low': case 'Informational': return value;
      default: return this.fail('INVALID_RISK_LEVEL', 'Unknown risk level.', 400);
    }
  }
  transitionInstance(id: string, input: Input<'transitionInstance'>): Observable<W.TransitionResultDto> {
    return this.mutate(id, row => {
      this.checkVersion(row, input.rowVersion);
      if (!input.toStatus) return this.fail('TO_STATUS_REQUIRED', 'Choose a target status.', 400);
      const allowed = row.availableTransitions?.find(t => t.toStatus === input.toStatus);
      if (!allowed?.permitted) return this.fail('WORKFLOW_INVALID_TRANSITION', 'The transition is not available.');
      if (allowed.requiresEvidence && !row.evidence.length) return this.fail('MISSING_EVIDENCE', 'Evidence is required.');
      if (allowed.requiresApproval) return this.fail('APPROVAL_REQUIRED', 'Approval is required.');
      return this.transition(row, input.toStatus, input.reason);
    });
  }
  assignInstance(id: string, input: Input<'assignInstance'>): Observable<W.AssignResultDto> {
    return this.mutate(id, row => {
      this.checkVersion(row, input.rowVersion);
      if (!input.assignToUserId && !input.assignToTeam) return this.fail('ASSIGNEE_REQUIRED', 'Choose an assignee or team.', 400);
      if (!input.reason?.trim()) return this.fail('ASSIGNMENT_REASON_REQUIRED', 'Enter an assignment reason.', 400);
      row.assignedTo = input.assignToUserId ?? input.assignToTeam ?? undefined;
      row.assignedToUserId = input.assignToUserId ?? undefined;
      this.transition(row, 'Assigned', input.reason);
      const taskId = row.remediation?.id ?? crypto.randomUUID();
      row.remediation = { id: taskId, title: row.title, assignedTo: row.assignedTo ?? '', team: input.assignToTeam ?? undefined, progress: 0, status: 'NotStarted', dueDate: row.sla.dueDate };
      this.tasks.delete(taskId);
      return { instanceId: row.id, status: 'Assigned', assignedToUserId: input.assignToUserId ?? null, assignedToTeam: input.assignToTeam ?? null, taskId, autoAssigned: false, code: null, rowVersion: row.rowVersion ?? '' };
    });
  }
  getRemediationTask(taskId: string): Observable<W.RemediationTaskDto> {
    return this.loadVulns().pipe(map(rows => {
      const cached = this.tasks.get(taskId); if (cached) return cached;
      const row = rows.find(r => r.remediation?.id === taskId), task = row?.remediation;
      if (!row || !task) return this.fail('TASK_NOT_FOUND', 'Task not found.', 404);
      const result: W.RemediationTaskDto = { id: task.id, instanceId: row.id, ownerUserId: row.assignedToUserId ?? null, ownerTeam: task.team ?? null, dueDate: task.dueDate ?? row.sla.dueDate, riskLevel: row.riskLevel, recommendation: task.title, rootCause: null, actionPlan: task.plan ?? null, targetDate: null, changeReference: null, progressPercent: task.progress, status: task.status, obstacle: null, targetDateExceedsSla: false, rowVersion: this.version(), createdAt: row.firstDetectedAt, updatedAt: null };
      this.tasks.set(taskId, result); return result;
    }));
  }
  updateRemediationTask(taskId: string, input: Input<'updateRemediationTask'>): Observable<W.RemediationTaskDto> {
    return this.getRemediationTask(taskId).pipe(switchMap(task => this.mutate(task.instanceId, row => {
      this.checkVersion(task, input.rowVersion);
      if (input.progressPercent != null && (input.progressPercent < 0 || input.progressPercent > 100)) return this.fail('INVALID_PROGRESS', 'Progress must be between 0 and 100.', 400);
      if (input.status && !['NotStarted', 'InProgress', 'Blocked', 'Completed'].includes(input.status)) return this.fail('INVALID_STATUS', 'Invalid task status.', 400);
      const { rowVersion: _, comment, ...changes } = input;
      Object.assign(task, Object.fromEntries(Object.entries(changes).filter(([, v]) => v != null)), { rowVersion: this.version(), updatedAt: this.now() });
      if (row.remediation) Object.assign(row.remediation, { progress: task.progressPercent, plan: task.actionPlan ?? undefined, status: task.status });
      if (comment) row.comments.push({ id: crypto.randomUUID(), body: comment, author: 'Demo operator', createdAt: this.now() });
      return task;
    })));
  }
  uploadEvidence(id: string, file: File, taskId?: string): Observable<Evidence> {
    return this.mutate(id, row => {
      if (!file.size) return this.fail('EVIDENCE_FILE_EMPTY', 'Choose a non-empty file.', 400);
      if (taskId && row.remediation?.id !== taskId) return this.fail('EVIDENCE_TASK_MISMATCH', 'Task belongs to another instance.');
      const evidence: Evidence = { id: crypto.randomUUID(), fileName: file.name, type: file.type, version: row.evidence.filter(e => e.fileName === file.name).length + 1, uploadedBy: 'Demo operator', uploadedAt: this.now(), scanResult: 'Pending' };
      row.evidence.push(evidence); return evidence;
    });
  }
  addComment(id: string, body: string): Observable<Comment> {
    return this.mutate(id, row => {
      if (!body.trim()) return this.fail('COMMENT_BODY_REQUIRED', 'Enter a comment.', 400);
      const comment = { id: crypto.randomUUID(), body, author: 'Demo operator', createdAt: this.now() };
      row.comments.push(comment); return comment;
    });
  }
  private riskResult(row: VulnerabilityInstance, score: number, level: RiskLevel, type: string, reason: string): W.InstanceRiskDto {
    const previousScore = row.riskScore, previousRiskLevel = row.riskLevel, historyId = crypto.randomUUID();
    row.riskScore = score; row.riskLevel = level; row.rowVersion = this.version();
    row.riskHistory.push({ id: historyId, score, riskLevel: level, calculationType: type, previousScore, reason, createdAt: this.now(), createdBy: 'Demo operator' });
    return { instanceId: row.id, score, riskLevel: level, calculationType: type, previousScore, previousRiskLevel, historyId, rowVersion: row.rowVersion, breakdown: null };
  }
  recalculateRisk(id: string, reason: string): Observable<W.InstanceRiskDto> {
    return this.mutate(id, row => {
      if (!row.riskBreakdown.length) return this.fail('RISK_INPUT_INCOMPLETE', 'No risk factors are available in this demo.');
      const score = Math.min(100, Math.max(0, row.riskBreakdown.reduce((sum, factor) => sum + factor.contribution, 0)));
      return this.riskResult(row, score, score >= 90 ? 'Critical' : score >= 70 ? 'High' : score >= 40 ? 'Medium' : score > 0 ? 'Low' : 'Informational', 'Auto', reason);
    });
  }
  adjustRisk(id: string, input: Input<'adjustRisk'>): Observable<W.InstanceRiskDto> {
    return this.mutate(id, row => {
      this.checkVersion(row, input.rowVersion);
      if (!input.reason?.trim()) return this.fail('RISK_REASON_REQUIRED', 'Enter a risk adjustment reason.', 400);
      if (input.score == null && !input.riskLevel) return this.fail('RISK_OVERRIDE_VALUE_REQUIRED', 'Enter a score or level.', 400);
      const score = input.score ?? row.riskScore;
      if (!Number.isFinite(score) || score < 0 || score > 100) return this.fail('INVALID_SCORE', 'Score must be between 0 and 100.', 400);
      const level = input.riskLevel ? this.demoRisk(input.riskLevel) : score >= 90 ? 'Critical' : score >= 70 ? 'High' : score >= 40 ? 'Medium' : score > 0 ? 'Low' : 'Informational';
      return this.riskResult(row, score, level, 'Manual', input.reason);
    });
  }
  submitVerification(id: string, input: Input<'submitVerification'>): Observable<W.TransitionResultDto> {
    return this.mutate(id, row => {
      this.checkVersion(row, input.rowVersion);
      if (!row.evidence.length) return this.fail('MISSING_EVIDENCE', 'Upload evidence before submission.');
      if (!['InProgress', 'Remediated'].includes(row.status)) return this.fail('WORKFLOW_INVALID_TRANSITION', 'The instance must be in progress.');
      return this.transition(row, 'PendingVerification', input.notes);
    });
  }
  rescanInstance(id: string): Observable<W.RescanResultDto> {
    return this.mutate(id, row => {
      if (row.status !== 'PendingVerification') return this.fail('RESCAN_NOT_ALLOWED', 'Submit the instance for verification first.');
      // Demo does not claim a scanner checked the target.
      return { requestId: crypto.randomUUID(), status: 'Failed', reopened: false, verificationId: null, fromStatus: row.status, toStatus: row.status, rowVersion: row.rowVersion ?? '' };
    });
  }
  recordVerification(id: string, input: Input<'recordVerification'>): Observable<W.VerificationRecordedDto> {
    return this.mutate(id, row => {
      this.checkVersion(row, input.rowVersion);
      if (row.status !== 'PendingVerification') return this.fail('VERIFICATION_NOT_PENDING', 'The instance is not pending verification.');
      if (!input.result || !['Verified', 'NotResolved', 'PartiallyResolved'].includes(input.result)) return this.fail('VERIFICATION_INVALID_RESULT', 'Choose Verified, NotResolved or PartiallyResolved.', 400);
      const transition = this.transition(row, input.result === 'Verified' ? 'Resolved' : 'Reopened', input.notes);
      const verificationId = crypto.randomUUID(), method = input.method ?? 'Manual';
      row.verification.push({ id: verificationId, method, result: input.result, independentVerifier: true, verifiedBy: 'Demo verifier', verifiedAt: this.now(), note: input.notes ?? undefined });
      return { verificationId, instanceId: row.id, result: input.result, method, isIndependent: true, fromStatus: transition.fromStatus, toStatus: transition.toStatus, rowVersion: transition.rowVersion };
    });
  }
  /** The browser demo does not model these server features; fail clearly instead of reporting a fake success. */
  private unsupported(feature: string): Observable<never> {
    return defer(() => this.fail('DEMO_UNSUPPORTED', `${feature} is not available in the browser demo. Use the API profile.`, 501));
  }
  getOperationsDashboard(): Observable<W.OperationsDashboardDto> { return this.unsupported('Operations dashboard'); }
  getExecutiveDashboard(): Observable<W.ExecutiveDashboardDto> { return this.unsupported('Executive dashboard'); }
  importFindings(_file: File, _transactionId: string): Observable<W.ImportResultDto> { return this.unsupported('File import'); }
  downloadImportTemplate(): Observable<Blob> { return this.unsupported('Import template download'); }
  createFinding(_input: Parameters<SvmApi['createFinding']>[0]): Observable<W.ManualFindingResultDto> { return this.unsupported('Manual finding registration'); }
  resolveDuplicate(_findingId: string, _input: Input<'resolveDuplicate'>): Observable<W.ResolveDuplicateResultDto> { return this.unsupported('Duplicate resolution'); }
  getMasterDataTypes(): Observable<W.MasterDataTypeDto[]> { return this.unsupported('Master data'); }
  getMasterData(_type: string): Observable<W.MasterDataItemDto[]> { return this.unsupported('Master data'); }
  saveMasterData(_type: string, _input: Input<'saveMasterData'>, _id?: string): Observable<W.MasterDataItemDto> { return this.unsupported('Master data changes'); }
  getStatusTransitions(): Observable<W.StatusTransitionDto[]> { return this.unsupported('Status transition configuration'); }
  saveStatusTransition(_input: Parameters<SvmApi['saveStatusTransition']>[0], _id?: string): Observable<W.StatusTransitionDto> { return this.unsupported('Status transition changes'); }
  getSlaPolicies(): Observable<W.SlaPolicyDto[]> { return this.unsupported('SLA policy configuration'); }
  saveSlaPolicy(_input: Parameters<SvmApi['saveSlaPolicy']>[0], _id?: string): Observable<W.SlaPolicyDto> { return this.unsupported('SLA policy changes'); }
  getAssignmentRules(): Observable<W.AssignmentRuleDto[]> { return this.unsupported('Assignment rule configuration'); }
  saveAssignmentRule(_input: Parameters<SvmApi['saveAssignmentRule']>[0], _id?: string): Observable<W.AssignmentRuleDto> { return this.unsupported('Assignment rule changes'); }
  getRiskWeights(): Observable<W.RiskWeightsDto> { return this.unsupported('Risk weight configuration'); }
  saveRiskWeights(_input: W.ReplaceRiskWeightsRequest): Observable<W.RiskWeightsDto> { return this.unsupported('Risk weight changes'); }
  validateRiskWeights(_input: W.ReplaceRiskWeightsRequest): Observable<W.RiskWeightsValidationDto> { return this.unsupported('Risk weight validation'); }
  getRiskThresholds(): Observable<W.RiskThresholdsDto> { return this.unsupported('Risk threshold configuration'); }
  saveRiskThresholds(_input: W.ReplaceRiskThresholdsRequest): Observable<W.RiskThresholdsDto> { return this.unsupported('Risk threshold changes'); }
  markNotificationRead(id: string): Observable<void> {
    return this.getNotifications().pipe(map(rows => { const row = rows.find(r => r.id === id); if (!row) this.fail('NOTIFICATION_NOT_FOUND', 'Notification not found.', 404); row.read = true; }));
  }
}
