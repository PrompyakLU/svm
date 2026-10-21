import { RiskLevel, SlaBreachStatus, VulnStatus } from '../core/models';

/** ป้ายภาษาไทยของสถานะ workflow */
export const STATUS_LABEL: Record<VulnStatus, string> = {
  UnderReview: 'อยู่ระหว่างพิจารณา',
  Resolved: 'แก้ไขและตรวจสอบแล้ว',
  ExceptionApproved: 'อนุมัติข้อยกเว้น',
  Archived: 'จัดเก็บ',
  New: 'ใหม่',
  Confirmed: 'ยืนยันแล้ว',
  FalsePositive: 'ผลบวกลวง',
  RiskAccepted: 'ยอมรับความเสี่ยง',
  Assigned: 'มอบหมายแล้ว',
  InProgress: 'กำลังแก้ไข',
  Remediated: 'แก้ไขแล้ว',
  PendingVerification: 'รอตรวจสอบ',
  Verified: 'ตรวจสอบแล้ว',
  Closed: 'ปิดงาน',
  Reopened: 'เปิดใหม่',
  Deferred: 'เลื่อน',
  Rejected: 'ปฏิเสธ',
};

export const RISK_LABEL: Record<RiskLevel, string> = {
  Critical: 'วิกฤต',
  High: 'สูง',
  Medium: 'ปานกลาง',
  Low: 'ต่ำ',
  Info: 'ข้อมูล',
  Informational: 'ข้อมูล',
};

export const SLA_LABEL: Record<SlaBreachStatus, string> = {
  NotStarted: 'ยังไม่เริ่ม',
  Paused: 'หยุดชั่วคราว',
  OnTrack: 'ตามกำหนด',
  NearDue: 'ใกล้ครบกำหนด',
  Breached: 'เกินกำหนด',
  Met: 'ทันกำหนด',
};

/** สีของแต่ละระดับความเสี่ยง (อ้างอิง CSS token --color-risk-*) */
export const RISK_COLOR_VAR: Record<RiskLevel, string> = {
  Critical: 'var(--color-risk-critical)',
  High: 'var(--color-risk-high)',
  Medium: 'var(--color-risk-medium)',
  Low: 'var(--color-risk-low)',
  Info: 'var(--color-risk-info)',
  Informational: 'var(--color-risk-info)',
};

export function slaColorVar(s: SlaBreachStatus): string {
  switch (s) {
    case 'Breached': return 'var(--color-risk-critical)';
    case 'NearDue': return 'var(--color-risk-medium)';
    case 'NotStarted':
    case 'Paused':
    case 'OnTrack': return 'var(--color-risk-info)';
    case 'Met': return 'var(--color-risk-low)';
  }
}
