/** Exact backend wire contracts. Screen mappings live in http-svm-api.ts. */

export interface CountDto {
  key: string;
  count: number;
}

export interface SlaSummaryDto {
  policyName: string;
  startedAt: string;
  dueDate: string;
  breachStatus: string;
  remainingHours: number;
  daysOverdue: number;
  running: boolean;
}

export interface InstanceListItemDto {
  id: string;
  code: string;
  title: string;
  assetId: string;
  assetCode: string;
  assetName: string;
  businessUnit: string | null;
  internetExposure: boolean;
  assetCriticality: string | null;
  cveId: string | null;
  cweId: string | null;
  location: string | null;
  port: number | null;
  component: string | null;
  status: string;
  riskScore: number | null;
  riskLevel: string | null;
  isKev: boolean;
  assignedToUserId: string | null;
  assignedTo: string | null;
  firstDetectedAt: string;
  lastDetectedAt: string;
  ageDays: number;
  sourceSystem: string | null;
  sla: SlaSummaryDto | null;
}

export interface InstanceAssetDto {
  id: string;
  assetCode: string;
  name: string;
  assetType: string;
  ipAddress: string | null;
  hostname: string | null;
  environment: string;
  businessUnit: string | null;
  criticality: string | null;
  internetExposure: boolean;
  assetStatus: string;
}

export interface RiskFactorDto {
  factor: string;
  weight: number;
  normalized: number;
  contribution: number;
  source: string | null;
}

export interface RiskHistoryEntryDto {
  id: string;
  score: number;
  riskLevel: string;
  calculationType: string;
  reason: string | null;
  previousScore: number | null;
  createdAt: string;
  createdByUserId: string | null;
  createdBy: string;
}

export interface StatusHistoryEntryDto {
  id: string;
  fromStatus: string | null;
  toStatus: string;
  reason: string | null;
  changedAt: string;
  changedByUserId: string;
  changedBy: string;
}

export interface EvidenceEntryDto {
  id: string;
  taskId: string | null;
  fileName: string;
  type: string;
  version: number;
  fileSize: number;
  scanResult: string;
  uploadedAt: string;
  uploadedByUserId: string;
  uploadedBy: string;
}

export interface CommentEntryDto {
  id: string;
  body: string;
  createdAt: string;
  createdByUserId: string;
  author: string;
}

export interface RemediationSummaryDto {
  id: string;
  title: string;
  assignedToUserId: string | null;
  assignedTo: string | null;
  team: string | null;
  dueDate: string;
  progress: number;
  plan: string | null;
  rootCause: string | null;
  targetDate: string | null;
  obstacle: string | null;
  status: string;
}

export interface VerificationEntryDto {
  id: string;
  method: string;
  result: string;
  independentVerifier: boolean;
  verifiedByUserId: string;
  verifiedBy: string;
  verifiedAt: string;
  note: string | null;
}

export interface AvailableTransitionDto {
  toStatus: string;
  requiresEvidence: boolean;
  requiresApproval: boolean;
  requiredPermission: string | null;
  permitted: boolean;
}

export interface InstanceDetailDto {
  id: string;
  code: string;
  title: string;
  rowVersion: string;
  asset: InstanceAssetDto;
  cveId: string | null;
  cweId: string | null;
  location: string | null;
  port: number | null;
  component: string | null;
  status: string;
  riskScore: number | null;
  riskLevel: string | null;
  isKev: boolean;
  assignedToUserId: string | null;
  assignedTo: string | null;
  firstDetectedAt: string;
  lastDetectedAt: string;
  ageDays: number;
  sourceSystem: string | null;
  updatedAt: string | null;
  sla: SlaSummaryDto | null;
  riskBreakdown: RiskFactorDto[];
  riskHistory: RiskHistoryEntryDto[];
  statusHistory: StatusHistoryEntryDto[];
  evidence: EvidenceEntryDto[];
  comments: CommentEntryDto[];
  remediation: RemediationSummaryDto | null;
  verification: VerificationEntryDto[];
  availableTransitions: AvailableTransitionDto[];
}

export interface OperationsSummaryDto {
  highOpen: number;
  totalOpen: number;
  unassigned: number;
  overdue: number;
  nearDue: number;
  pendingVerification: number;
  reopened: number;
  criticalOpen: number;
  kevOpen: number;
  myOpenTasks: number;
  importErrors: number;
  integrationErrors: number | null;
}

export interface OperationsLists {
  unassigned: InstanceListItemDto[];
  overdue: InstanceListItemDto[];
  pendingVerification: InstanceListItemDto[];
  myTasks: InstanceListItemDto[];
}

export interface OperationsDashboardDto {
  byRiskLevel: CountDto[];
  byStatus: CountDto[];
  byBusinessUnit: CountDto[];
  slaByStatus: CountDto[];
  meanTimeToRemediateDays: number | null;
  trend: WeeklyTrendPoint[];
  generatedAt: string;
  organizationWide: boolean;
  summary: OperationsSummaryDto;
  lists: OperationsLists;
}

export interface ExecutiveTotalsDto {
  totalInstances: number;
  totalOpen: number;
  criticalOpen: number;
  highOpen: number;
  kevOpen: number;
  internetExposedOpen: number;
  unassignedOpen: number;
  slaBreached: number;
  slaNearDue: number;
  slaCompliancePercent: number | null;
  meanTimeToRemediateDays: number | null;
}

export interface WeeklyTrendPoint {
  weekStart: string;
  new: number;
  newCritical: number;
  resolved: number;
}

export interface TopRiskAssetDto {
  assetId: string;
  assetCode: string;
  name: string;
  businessUnit: string | null;
  openCount: number;
  criticalCount: number;
  highCount: number;
  maxRiskScore: number | null;
  kevCount: number;
}

export interface ExecutiveDashboardDto {
  generatedAt: string;
  organizationWide: boolean;
  weeks: number;
  totals: ExecutiveTotalsDto;
  byRiskLevel: CountDto[];
  byStatus: CountDto[];
  byBusinessUnit: CountDto[];
  slaByStatus: CountDto[];
  overdueAging: CountDto[];
  trend: WeeklyTrendPoint[];
  topRiskAssets: TopRiskAssetDto[];
}

export interface MasterDataTypeDto {
  type: string;
  name: string;
  readOnly: boolean;
}

export interface MasterDataItemDto {
  id: string | null;
  code: string;
  description: string | null;
  isActive: boolean;
  readOnly: boolean;
  version: string;
}

export interface MasterDataRequest {
  code: string | null;
  description: string | null;
  isActive: boolean | null;
  version: string | null;
}

export interface StatusTransitionDto {
  id: string;
  fromStatus: string;
  toStatus: string;
  requiredPermission: string | null;
  requiresEvidence: boolean;
  requiresApproval: boolean;
  isActive: boolean;
  version: string;
}

export interface CreateStatusTransitionRequest {
  fromStatus: string | null;
  toStatus: string | null;
  requiredPermission: string | null;
  requiresEvidence: boolean | null;
  requiresApproval: boolean | null;
  isActive: boolean | null;
}

export interface UpdateStatusTransitionRequest {
  requiredPermission: string | null;
  requiresEvidence: boolean | null;
  requiresApproval: boolean | null;
  isActive: boolean | null;
  version: string | null;
}

export interface SlaPolicyDto {
  id: string;
  riskLevel: string;
  assetCriticality: string | null;
  internetExposure: boolean | null;
  kevOnly: boolean;
  acknowledgeHours: number;
  startHours: number;
  resolveHours: number;
  isActive: boolean;
  version: string;
}

export interface SlaPolicyRequest {
  riskLevel: string | null;
  assetCriticality: string | null;
  internetExposure: boolean | null;
  kevOnly: boolean | null;
  acknowledgeHours: number | null;
  startHours: number | null;
  resolveHours: number | null;
  isActive: boolean | null;
  version: string | null;
}

export interface RiskWeightDto {
  factor: string;
  weight: number;
  isActive: boolean;
  updatedAt: string | null;
}

export interface RiskWeightsDto {
  weights: RiskWeightDto[];
  activeSum: number;
  version: string;
}

export interface RiskWeightItem {
  factor: string | null;
  weight: number | null;
  isActive: boolean | null;
}

export interface ReplaceRiskWeightsRequest {
  weights: RiskWeightItem[] | null;
  version: string | null;
}

export interface RiskThresholdDto {
  riskLevel: string;
  minScore: number;
  maxScore: number;
}

export interface RiskThresholdsDto {
  thresholds: RiskThresholdDto[];
  version: string;
}

export interface RiskThresholdItem {
  riskLevel: string | null;
  minScore: number | null;
  maxScore: number | null;
}

export interface ReplaceRiskThresholdsRequest {
  thresholds: RiskThresholdItem[] | null;
  version: string | null;
}

export interface RiskWeightsValidationDto {
  isValid: boolean;
  activeSum: number;
  issues: string[];
}

export interface AssignmentRuleDto {
  id: string;
  matchType: string;
  matchValue: string;
  assignToUserId: string | null;
  assignToTeam: string | null;
  priority: number;
  isActive: boolean;
  version: string;
}

export interface AssignmentRuleRequest {
  matchType: string | null;
  matchValue: string | null;
  assignToUserId: string | null;
  assignToTeam: string | null;
  priority: number | null;
  isActive: boolean | null;
  version: string | null;
}

export interface ManualFindingRequest {
  assetCode: string | null;
  sourceSystem: string | null;
  sourceRecordId: string | null;
  findingName: string | null;
  severity: string | null;
  cveId: string | null;
  cweId: string | null;
  cvssVersion: string | null;
  cvssBaseScore: number | null;
  cvssVector: string | null;
  location: string | null;
  port: number | null;
  component: string | null;
  detectedAt: string | null;
}

export interface ImportRowError {
  row: number;
  reason: string;
}

export interface ImportResultDto {
  batchId: string;
  status: string;
  totalRows: number;
  successCount: number;
  failedCount: number;
  createdCount: number;
  duplicateCount: number;
  queuedCount: number;
  needsReviewCount: number;
  replayed: boolean;
  errors: ImportRowError[];
}

export interface ImportBatchDto {
  id: string;
  fileName: string;
  transactionId: string | null;
  totalRows: number;
  successCount: number;
  failedCount: number;
  status: string;
  importedBy: string;
  importedAt: string;
}

export interface ManualFindingResultDto {
  findingId: string;
  instanceId: string | null;
  outcome: string;
  isDuplicate: boolean;
  validationStatus: string;
  reviewIssues: string[];
}

export interface DedupCandidateDto {
  instanceId: string;
  title: string;
  cveId: string | null;
  location: string | null;
  port: number | null;
  component: string | null;
  status: string;
  lastDetectedAt: string;
}

export interface DedupQueueItemDto {
  findingId: string;
  assetId: string | null;
  assetCode: string | null;
  findingName: string;
  sourceSystem: string;
  sourceRecordId: string | null;
  severity: string | null;
  cveId: string | null;
  location: string | null;
  port: number | null;
  component: string | null;
  detectedAt: string;
  importBatchId: string | null;
  candidates: DedupCandidateDto[];
}

export interface ResolveDuplicateRequest {
  decision: string | null;
  instanceId: string | null;
  reason: string | null;
}

export interface ResolveDuplicateResultDto {
  findingId: string;
  instanceId: string;
  decision: string;
  validationStatus: string;
}

export interface AssignRequest {
  assignToUserId: string | null;
  assignToTeam: string | null;
  reason: string | null;
  rowVersion: string | null;
}

export interface AssignResultDto {
  instanceId: string;
  status: string;
  assignedToUserId: string | null;
  assignedToTeam: string | null;
  taskId: string | null;
  autoAssigned: boolean;
  code: string | null;
  rowVersion: string;
}

export interface UpdateTaskRequest {
  rootCause: string | null;
  actionPlan: string | null;
  targetDate: string | null;
  changeReference: string | null;
  progressPercent: number | null;
  status: string | null;
  obstacle: string | null;
  comment: string | null;
  rowVersion: string | null;
}

export interface RemediationTaskDto {
  id: string;
  instanceId: string;
  ownerUserId: string | null;
  ownerTeam: string | null;
  dueDate: string;
  riskLevel: string;
  recommendation: string;
  rootCause: string | null;
  actionPlan: string | null;
  targetDate: string | null;
  changeReference: string | null;
  progressPercent: number;
  status: string;
  obstacle: string | null;
  targetDateExceedsSla: boolean;
  rowVersion: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface EvidenceDto {
  id: string;
  instanceId: string;
  taskId: string | null;
  version: number;
  fileName: string;
  contentType: string;
  fileSize: number;
  scanStatus: string;
  sha256: string | null;
  uploadedBy: string;
  uploadedAt: string;
}

export interface MentionDto {
  userId: string;
  username: string;
}

export interface CommentDto {
  id: string;
  instanceId: string;
  body: string;
  mentions: MentionDto[];
  createdBy: string;
  createdAt: string;
}

export interface SubmitVerificationRequest {
  rowVersion: string | null;
  notes: string | null;
}

export interface RecordVerificationRequest {
  result: string | null;
  method: string | null;
  notes: string | null;
  rowVersion: string | null;
}

export interface VerificationRecordedDto {
  verificationId: string;
  instanceId: string;
  result: string;
  method: string;
  isIndependent: boolean;
  fromStatus: string;
  toStatus: string;
  rowVersion: string;
}

export interface RescanResultDto {
  requestId: string;
  status: string;
  reopened: boolean;
  verificationId: string | null;
  fromStatus: string;
  toStatus: string;
  rowVersion: string;
}

export interface RiskFactorScore {
  factor: string;
  normalized: number;
  weight: number;
  contribution: number;
  source: string;
}

export interface RiskBreakdown {
  model: string;
  factors: RiskFactorScore[];
  rawScore: number;
  score: number;
  riskLevel: string;
}

export interface RecalculateRiskRequest {
  rowVersion: string | null;
}

export interface OverrideRiskRequest {
  score: number | null;
  riskLevel: string | null;
  reason: string | null;
  rowVersion: string | null;
}

export interface InstanceRiskDto {
  instanceId: string;
  score: number;
  riskLevel: string;
  calculationType: string;
  previousScore: number | null;
  previousRiskLevel: string | null;
  historyId: string;
  rowVersion: string;
  breakdown: RiskBreakdown | null;
}

export interface TransitionRequest {
  toStatus: string | null;
  reason: string | null;
  rowVersion: string | null;
}

export interface TransitionResultDto {
  instanceId: string;
  fromStatus: string;
  toStatus: string;
  historyId: string;
  changedAt: string;
  rowVersion: string;
}

export interface AssetOwnerDto {
  id: string;
  ownerType: string;
  ownerUserId: string | null;
  ownerName: string | null;
  teamName: string | null;
}

export interface AssetDto {
  id: string;
  assetCode: string;
  name: string;
  assetType: string;
  ipAddress: string | null;
  hostname: string | null;
  url: string | null;
  environment: string;
  businessUnit: string | null;
  criticality: string | null;
  internetExposure: boolean;
  status: string;
  cloudResourceId: string | null;
  repositoryId: string | null;
  owners: AssetOwnerDto[];
  warnings: string[];
  createdAt: string;
  updatedAt: string | null;
}

export interface AuditRecordDto {
  id: number;
  userId: string | null;
  action: string;
  objectType: string;
  objectId: string;
  beforeValue: string | null;
  afterValue: string | null;
  result: string;
  source: string;
  timestamp: string;
  prevHash: string | null;
  rowHash: string;
}

export interface NotificationDto {
  id: string;
  type: string;
  subject: string;
  body: string;
  relatedInstanceId: string | null;
  isRead: boolean;
  createdAt: string;
}
