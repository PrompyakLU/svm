import { Observable } from 'rxjs';
import * as Wire from './server-contracts';
export * from './server-contracts';

type OptionalNullable<T> = { [K in keyof T as null extends T[K] ? never : K]: T[K] } & { [K in keyof T as null extends T[K] ? K : never]?: T[K] };
export type CreateFindingRequest = OptionalNullable<Wire.ManualFindingRequest>;
export type ImportResult = Wire.ImportResultDto;
export type ManualFindingResult = Wire.ManualFindingResultDto;
export type ResolveDuplicateResult = Wire.ResolveDuplicateResultDto;
export type TransitionResult = Wire.TransitionResultDto;
export type AssignResult = Wire.AssignResultDto;
export type RemediationTaskResult = Wire.RemediationTaskDto;
export type InstanceRiskResult = Wire.InstanceRiskDto;
export type RescanResult = Wire.RescanResultDto;
export type VerificationRecordedResult = Wire.VerificationRecordedDto;

export interface ProblemDetails { type?: string; title?: string; status?: number; detail?: string; instance?: string; code?: string; requestId?: string; errors?: Record<string, string[]>; }

import {
  AdminData, Asset, AuditEntry, DashboardData, DedupCandidate, ImportBatch,
  NotificationItem, Page, VulnerabilityInstance, VulnQuery, Evidence, Comment,
} from '../models';

/**
 * สัญญา (contract) ของ data layer — ใช้ร่วมทั้ง mock และ API จริง.
 * Wave 2: MockSvmApi (อ่าน JSON). Wave 6: HttpSvmApi (เรียก /api/v1).
 * สลับผ่าน provideSvmApi() ด้วย environment.useMock.
 */
export abstract class SvmApi {
  abstract getOperationsDashboard(): Observable<Wire.OperationsDashboardDto>;
  abstract getExecutiveDashboard(): Observable<Wire.ExecutiveDashboardDto>;
  abstract importFindings(file: File, transactionId: string): Observable<ImportResult>;
  abstract downloadImportTemplate(): Observable<Blob>;
  abstract createFinding(input: CreateFindingRequest): Observable<ManualFindingResult>;
  abstract resolveDuplicate(findingId: string, input: OptionalNullable<Wire.ResolveDuplicateRequest>): Observable<ResolveDuplicateResult>;
  abstract transitionInstance(id: string, input: OptionalNullable<Wire.TransitionRequest>): Observable<TransitionResult>;
  abstract assignInstance(id: string, input: OptionalNullable<Wire.AssignRequest>): Observable<AssignResult>;
  abstract getRemediationTask(taskId: string): Observable<RemediationTaskResult>;
  abstract updateRemediationTask(taskId: string, input: OptionalNullable<Wire.UpdateTaskRequest>): Observable<RemediationTaskResult>;
  abstract uploadEvidence(id: string, file: File, taskId?: string): Observable<Evidence>;
  abstract addComment(id: string, body: string): Observable<Comment>;
  /** The backend has no recalculation reason field; reason is retained for caller compatibility. */
  abstract recalculateRisk(id: string, reason: string): Observable<InstanceRiskResult>;
  abstract adjustRisk(id: string, input: OptionalNullable<Wire.OverrideRiskRequest>): Observable<InstanceRiskResult>;
  abstract submitVerification(id: string, input: OptionalNullable<Wire.SubmitVerificationRequest>): Observable<TransitionResult>;
  abstract rescanInstance(id: string): Observable<RescanResult>;
  abstract recordVerification(id: string, input: OptionalNullable<Wire.RecordVerificationRequest>): Observable<VerificationRecordedResult>;
  abstract markNotificationRead(id: string): Observable<void>;
  abstract getMasterDataTypes(): Observable<Wire.MasterDataTypeDto[]>;
  abstract getMasterData(type: string): Observable<Wire.MasterDataItemDto[]>;
  abstract saveMasterData(type: string, input: OptionalNullable<Wire.MasterDataRequest>, id?: string): Observable<Wire.MasterDataItemDto>;
  abstract getStatusTransitions(): Observable<Wire.StatusTransitionDto[]>;
  abstract saveStatusTransition(input: OptionalNullable<Wire.CreateStatusTransitionRequest & Wire.UpdateStatusTransitionRequest>, id?: string): Observable<Wire.StatusTransitionDto>;
  abstract getSlaPolicies(): Observable<Wire.SlaPolicyDto[]>;
  abstract saveSlaPolicy(input: OptionalNullable<Wire.SlaPolicyRequest>, id?: string): Observable<Wire.SlaPolicyDto>;
  abstract getAssignmentRules(): Observable<Wire.AssignmentRuleDto[]>;
  abstract saveAssignmentRule(input: OptionalNullable<Wire.AssignmentRuleRequest>, id?: string): Observable<Wire.AssignmentRuleDto>;
  abstract getRiskWeights(): Observable<Wire.RiskWeightsDto>;
  abstract saveRiskWeights(input: Wire.ReplaceRiskWeightsRequest): Observable<Wire.RiskWeightsDto>;
  abstract validateRiskWeights(input: Wire.ReplaceRiskWeightsRequest): Observable<Wire.RiskWeightsValidationDto>;
  abstract getRiskThresholds(): Observable<Wire.RiskThresholdsDto>;
  abstract saveRiskThresholds(input: Wire.ReplaceRiskThresholdsRequest): Observable<Wire.RiskThresholdsDto>;

  abstract getDashboard(): Observable<DashboardData>;
  abstract getVulnerabilities(query: VulnQuery): Observable<Page<VulnerabilityInstance>>;
  abstract getVulnerability(id: string): Observable<VulnerabilityInstance | undefined>;
  abstract getBusinessUnits(): Observable<string[]>;
  abstract getAssets(): Observable<Asset[]>;
  abstract getImportBatches(): Observable<ImportBatch[]>;
  abstract getDedupQueue(): Observable<DedupCandidate[]>;
  abstract getAudit(search: string): Observable<AuditEntry[]>;
  abstract getNotifications(): Observable<NotificationItem[]>;
  abstract getAdminData(): Observable<AdminData>;
}
