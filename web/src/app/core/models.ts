/**
 * โมเดลข้อมูลฝั่ง FE (camelCase ตาม API JSON — IRPC naming).
 * สอดคล้องกับ design/data-model.md (core slice).
 */

export type RiskLevel = 'Critical' | 'High' | 'Medium' | 'Low' | 'Info' | 'Informational';

export const RISK_LEVELS: readonly RiskLevel[] = ['Critical', 'High', 'Medium', 'Low', 'Informational'];

/** 13 สถานะของ workflow (design C7 / FR-22) */
export type VulnStatus =
  | 'UnderReview'
  | 'Resolved'
  | 'ExceptionApproved'
  | 'Archived'
  | 'New'
  | 'Confirmed'
  | 'FalsePositive'
  | 'RiskAccepted'
  | 'Assigned'
  | 'InProgress'
  | 'Remediated'
  | 'PendingVerification'
  | 'Verified'
  | 'Closed'
  | 'Reopened'
  | 'Deferred'
  | 'Rejected';

export const VULN_STATUSES: readonly VulnStatus[] = [
  'UnderReview', 'Resolved', 'ExceptionApproved', 'Archived',
  'New', 'Confirmed', 'FalsePositive', 'RiskAccepted', 'Assigned', 'InProgress',
  'Remediated', 'PendingVerification', 'Verified', 'Closed', 'Reopened', 'Deferred', 'Rejected',
];

export type SlaBreachStatus = 'OnTrack' | 'NearDue' | 'Breached' | 'Met' | 'NotStarted' | 'Paused';

export interface Asset {
  id: string;
  assetCode: string;
  name: string;
  assetType: string;
  ipAddress?: string;
  hostname?: string;
  environment: string;
  businessUnit: string;
  criticality: RiskLevel;
  internetExposure: boolean;
  status: string;
}

export interface RiskFactor {
  factor: string;
  weight: number;
  rawValue?: number;
  source?: string | null;
  normalized: number;
  contribution: number;
}

export interface RiskScoreHistoryEntry {
  id: string;
  score: number;
  riskLevel: RiskLevel;
  calculationType: string;
  reason?: string;
  previousScore?: number;
  createdAt: string;
  createdBy: string;
}

export interface StatusHistoryEntry {
  id: string;
  fromStatus: VulnStatus | null;
  toStatus: VulnStatus;
  reason?: string;
  changedBy: string;
  changedAt: string;
}

export interface Evidence {
  id: string;
  fileName: string;
  type: string;
  version: number;
  uploadedBy: string;
  uploadedAt: string;
  scanResult: string;
  note?: string;
}

export interface Comment {
  id: string;
  author: string;
  body: string;
  createdAt: string;
}

export interface RemediationTask {
  id: string;
  title: string;
  assignedTo: string;
  team?: string;
  dueDate?: string;
  progress: number;
  plan?: string;
  status: string;
}

export interface SlaTimer {
  policyName: string;
  startedAt: string;
  dueDate: string;
  breachStatus: SlaBreachStatus;
  remainingHours: number;
}

export interface VerificationRecord {
  id: string;
  method: string;
  result: string;
  verifiedBy?: string;
  verifiedAt?: string;
  note?: string;
  independentVerifier: boolean;
}

export interface AvailableTransition {
  toStatus: VulnStatus;
  requiresEvidence: boolean;
  requiresApproval: boolean;
  requiredPermission: string | null;
  permitted: boolean;
}

export interface VulnerabilityInstance {
  rowVersion?: string;
  availableTransitions?: AvailableTransition[];
  assignedToUserId?: string;
  riskUnscored?: boolean;
  id: string;
  code: string;
  title: string;
  assetId: string;
  assetCode: string;
  assetName: string;
  businessUnit: string;
  cveId?: string;
  cweId?: string;
  location: string;
  port?: number;
  component?: string;
  status: VulnStatus;
  riskScore: number;
  riskLevel: RiskLevel;
  isKev: boolean;
  assignedTo?: string;
  firstDetectedAt: string;
  lastDetectedAt: string;
  sourceSystem: string;
  sla: SlaTimer;
  riskBreakdown: RiskFactor[];
  riskHistory: RiskScoreHistoryEntry[];
  statusHistory: StatusHistoryEntry[];
  evidence: Evidence[];
  comments: Comment[];
  remediation?: RemediationTask;
  verification: VerificationRecord[];
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface VulnQuery {
  search?: string;
  status?: VulnStatus | '';
  riskLevel?: RiskLevel | '';
  businessUnit?: string | '';
  kevOnly?: boolean;
  slaStatus?: SlaBreachStatus | '';
  openOnly?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

/** ---- Dashboard ---- */
export interface Count {
  key: string;
  count: number;
}

export interface DashboardData {
  generatedAt?: string;
  organizationWide?: boolean;
  trendPeriod?: 'day' | 'week';
  trendMeaning?: 'open' | 'new';
  canReadInstances?: boolean;
  totalOpen: number;
  critical: number;
  high: number;
  slaBreaches: number;
  kevOpen: number;
  meanTimeToRemediateDays: number | null;
  byRiskLevel: Count[];
  byStatus: Count[];
  byBusinessUnit: Count[];
  slaByStatus: Count[];
  trendLast7Days: Count[];
}

/** ---- Intake ---- */
export interface ImportBatch {
  id: string;
  fileName: string;
  status: string;
  totalRows: number;
  successCount: number;
  failedCount: number;
  importedBy: string;
  importedAt: string;
  errorDetail?: string;
}

export interface DedupCandidate {
  findingId?: string;
  candidates?: import('./api/server-contracts').DedupCandidateDto[];
  id: string;
  findingName: string;
  assetCode: string;
  cveId?: string;
  location: string;
  existingInstanceCode: string;
  matchScore?: number;
}

/** ---- Audit ---- */
export interface AuditEntry {
  id: string;
  timestamp: string;
  actor: string;
  action: string;
  objectType: string;
  objectId: string;
  result: string;
  requestId: string;
}

/** ---- Notification ---- */
export interface NotificationItem {
  id: string;
  type: string;
  title: string;
  body: string;
  createdAt: string;
  read: boolean;
  channel: 'InApp' | 'Email' | 'Teams';
}

/** ---- Admin / config ---- */
export interface RiskWeight {
  id: string;
  factor: string;
  weight: number;
  isActive: boolean;
}
export interface RiskThreshold {
  riskLevel: RiskLevel;
  minScore: number;
  maxScore: number;
}
export interface SlaPolicy {
  id: string;
  riskLevel: RiskLevel;
  remediationDays: number;
  verificationDays: number;
}
export interface StatusTransition {
  id: string;
  fromStatus: VulnStatus;
  toStatus: VulnStatus;
  requiredPermission?: string;
  requiresEvidence: boolean;
  requiresApproval: boolean;
  isActive: boolean;
}
export interface AssignmentRule {
  id: string;
  matchType: string;
  matchValue: string;
  assignTo: string;
  priority: number;
  isActive: boolean;
}
export interface UserRow {
  id: string;
  username: string;
  displayName: string;
  email: string;
  roles: string[];
  isActive: boolean;
}
export interface AdminData {
  riskWeights: RiskWeight[];
  riskThresholds: RiskThreshold[];
  slaPolicies: SlaPolicy[];
  statusTransitions: StatusTransition[];
  assignmentRules: AssignmentRule[];
  users: UserRow[];
}
