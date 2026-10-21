import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import {
  SvmApi, RiskWeightsDto, RiskThresholdsDto, StatusTransitionDto, SlaPolicyDto,
  AssignmentRuleDto, MasterDataTypeDto, MasterDataItemDto,
} from '../../core/api/svm-api';
import { StateBlock } from '../../shared/ui/ui';
import { RISK_LEVELS, VULN_STATUSES } from '../../core/models';
import { ActionState, actionError } from '../action-support';

type Tab = 'weights' | 'thresholds' | 'sla' | 'transitions' | 'rules' | 'master';
@Component({
  selector: 'app-admin',
  imports: [FormsModule, NgTemplateOutlet, StateBlock],
  templateUrl: './admin.html',
  styles: [':host{display:block}.tabs{display:flex;gap:4px;flex-wrap:wrap;border-bottom:1px solid var(--color-bg-border);margin-bottom:var(--space-md)}.tab{background:transparent;border:0;border-bottom:2px solid transparent;padding:10px 14px;min-height:44px;cursor:pointer;color:var(--color-fg-secondary)}.tab--on{color:var(--color-brand-primary);border-bottom-color:var(--color-brand-primary)}.controls{display:flex;gap:var(--space-sm);flex-wrap:wrap;align-items:end;margin-block:var(--space-sm)}.editor{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:var(--space-sm)}.field{min-width:0}input,select{max-width:100%}td{min-width:110px}@media(max-width:640px){.editor{grid-template-columns:1fr}}.invalid{color:var(--color-risk-critical)}.hint-error{margin:var(--space-xs) 0 0;color:var(--color-risk-critical);font-size:var(--fs-body-sm)}'],
})
export class Admin {
  private readonly api = inject(SvmApi);
  protected readonly action = new ActionState();
  protected readonly tab = signal<Tab>('weights');
  protected readonly loading = signal(false);
  protected readonly readError = signal('');
  protected readonly riskLevels = RISK_LEVELS.filter(level => level !== 'Info');
  protected readonly statuses = VULN_STATUSES.filter(status => !['Remediated', 'Verified', 'Deferred', 'Rejected'].includes(status));
  protected readonly tabs: { key: Tab; label: string; permission: string }[] = [
    { key: 'weights', label: 'น้ำหนักความเสี่ยง', permission: 'admin.risk' },
    { key: 'thresholds', label: 'เกณฑ์ระดับ', permission: 'admin.risk' },
    { key: 'sla', label: 'นโยบาย SLA', permission: 'admin.sla' },
    { key: 'transitions', label: 'สถานะ', permission: 'admin.workflow' },
    { key: 'rules', label: 'กฎมอบหมาย', permission: 'admin.assignment' },
    { key: 'master', label: 'ข้อมูลหลัก', permission: 'admin.masterdata' },
  ];
  protected weights: RiskWeightsDto | null = null;
  protected thresholds: RiskThresholdsDto | null = null;
  protected transitions: StatusTransitionDto[] = [];
  protected sla: SlaPolicyDto[] = [];
  protected rules: AssignmentRuleDto[] = [];
  protected masterTypes: MasterDataTypeDto[] = [];
  protected masterItems: MasterDataItemDto[] = [];
  protected masterType = '';
  protected transitionDraft = this.newTransition();
  protected slaDraft = this.newSla();
  protected ruleDraft = this.newRule();
  protected masterDraft = { code: '', description: '', isActive: true };
  constructor() {
    const first = this.tabs.find(t => this.action.can(t.permission));
    if (first) { this.tab.set(first.key); void this.load(); }
    else this.readError.set('คุณไม่มีสิทธิ์จัดการการตั้งค่าระบบ');
  }
  private newTransition() { return { fromStatus: 'New', toStatus: 'UnderReview', requiredPermission: 'instance.transition', requiresEvidence: false, requiresApproval: false, isActive: true }; }
  private newSla() { return { riskLevel: 'High', assetCriticality: null as string | null, internetExposure: null as boolean | null, kevOnly: false, acknowledgeHours: 8, startHours: 24, resolveHours: 168, isActive: true }; }
  private newRule() { return { matchType: 'BusinessUnit', matchValue: '', assignToUserId: null as string | null, assignToTeam: null as string | null, priority: 1, isActive: true }; }
  protected select(tab: Tab): void { if (this.action.busy()) return; this.tab.set(tab); this.action.error.set(''); this.action.success.set(''); void this.load(); }
  protected canEdit(): boolean { return this.action.can(this.tabs.find(t => t.key === this.tab())!.permission); }
  protected isMasterReadOnly(): boolean { return this.masterTypes.find(t => t.type === this.masterType)?.readOnly ?? true; }
  protected weightSum(): number {
    const sum = this.weights?.weights.filter(w => w.isActive).reduce((total, w) => total + Number(w.weight), 0) ?? 0;
    return Number.isFinite(sum) ? sum : NaN;
  }
  /** Active weights must sum to 1 (±0.0001) and every weight must be a valid 0–1 number. */
  protected weightsValid(): boolean {
    const w = this.weights?.weights ?? [];
    if (!w.every(x => Number.isFinite(Number(x.weight)) && Number(x.weight) >= 0 && Number(x.weight) <= 1)) return false;
    const sum = this.weightSum();
    return Number.isFinite(sum) && Math.abs(sum - 1) <= 0.0001;
  }
  /** Each tier needs min < max within 0–100; the set must cover 0–100 with no gap or overlap. */
  protected thresholdsValid(): boolean {
    const rows = this.thresholds?.thresholds ?? [];
    if (rows.length === 0) return false;
    const spans = rows.map(r => ({ min: Number(r.minScore), max: Number(r.maxScore) }));
    if (!spans.every(s => Number.isFinite(s.min) && Number.isFinite(s.max) && s.min >= 0 && s.max <= 100 && s.min < s.max)) return false;
    const sorted = [...spans].sort((a, b) => a.min - b.min);
    if (sorted[0].min !== 0 || sorted[sorted.length - 1].max !== 100) return false;
    return sorted.every((s, i) => i === 0 || Math.abs(s.min - sorted[i - 1].max) <= 0.0001);
  }
  /** SLA hours must be positive and ordered acknowledge <= start <= resolve. */
  protected slaValid(row: { acknowledgeHours: number; startHours: number; resolveHours: number }): boolean {
    const a = Number(row.acknowledgeHours), s = Number(row.startHours), r = Number(row.resolveHours);
    if (![a, s, r].every(n => Number.isFinite(n) && n >= 1)) return false;
    return a <= s && s <= r;
  }
  /** A rule needs a match value and exactly one assignee target (user XOR team). */
  protected ruleValid(row: { matchValue: string; assignToUserId: string | null; assignToTeam: string | null }): boolean {
    if (!row.matchValue?.trim()) return false;
    const hasUser = !!row.assignToUserId?.trim();
    const hasTeam = !!row.assignToTeam?.trim();
    return hasUser !== hasTeam;
  }
  protected async load(): Promise<void> {
    if (!this.canEdit()) { this.readError.set('คุณไม่มีสิทธิ์อ่านการตั้งค่านี้'); return; }
    this.loading.set(true); this.readError.set('');
    try {
      switch (this.tab()) {
        case 'weights': this.weights = await firstValueFrom(this.api.getRiskWeights()); break;
        case 'thresholds': this.thresholds = await firstValueFrom(this.api.getRiskThresholds()); break;
        case 'transitions': this.transitions = await firstValueFrom(this.api.getStatusTransitions()); break;
        case 'sla': this.sla = await firstValueFrom(this.api.getSlaPolicies()); break;
        case 'rules': this.rules = await firstValueFrom(this.api.getAssignmentRules()); break;
        case 'master':
          this.masterTypes = await firstValueFrom(this.api.getMasterDataTypes());
          if (!this.masterType || !this.masterTypes.some(t => t.type === this.masterType)) this.masterType = this.masterTypes[0]?.type ?? '';
          this.masterItems = this.masterType ? await firstValueFrom(this.api.getMasterData(this.masterType)) : []; break;
      }
    } catch (error) { this.readError.set(actionError(error)); }
    finally { this.loading.set(false); }
  }
  protected async saveWeights(validate = false): Promise<void> {
    if (!this.weights || !this.action.can('admin.risk')) return;
    if (!validate && !this.weightsValid()) { this.action.error.set('น้ำหนักที่ใช้งานต้องรวมได้ 1 และแต่ละค่าต้องอยู่ในช่วง 0–1'); return; }
    const input = { weights: this.weights.weights.map(w => ({ factor: w.factor, weight: Number(w.weight), isActive: w.isActive })), version: this.weights.version };
    await this.action.run(async () => {
      if (validate) { const r = await firstValueFrom(this.api.validateRiskWeights(input)); return r.isValid ? 'น้ำหนักถูกต้อง · ผลรวม ' + r.activeSum : 'น้ำหนักไม่ถูกต้อง · ' + r.issues.join(', '); }
      this.weights = await firstValueFrom(this.api.saveRiskWeights(input));
      return 'บันทึกน้ำหนักความเสี่ยงสำเร็จ';
    }, 'บันทึกน้ำหนักความเสี่ยงสำเร็จ');
  }
  protected async saveThresholds(): Promise<void> {
    if (!this.thresholds || !this.action.can('admin.risk')) return;
    if (!this.thresholdsValid()) { this.action.error.set('ช่วงคะแนนต้องครอบคลุม 0–100 ต่อเนื่อง ไม่ซ้อนหรือมีช่องว่าง และคะแนนต่ำสุดต้องน้อยกว่าสูงสุด'); return; }
    await this.action.run(async () => { this.thresholds = await firstValueFrom(this.api.saveRiskThresholds({ thresholds: this.thresholds!.thresholds, version: this.thresholds!.version })); }, 'บันทึกเกณฑ์ระดับสำเร็จ');
  }
  protected async saveTransition(row?: StatusTransitionDto): Promise<void> {
    if (!this.action.can('admin.workflow')) return;
    await this.action.run(async () => {
      const r = await firstValueFrom(this.api.saveStatusTransition(row ?? this.transitionDraft, row?.id));
      this.transitions = row ? this.transitions.map(t => t.id === r.id ? r : t) : [...this.transitions, r];
      if (!row) this.transitionDraft = this.newTransition();
    }, 'บันทึกสถานะสำเร็จ');
  }
  protected async saveSla(row?: SlaPolicyDto): Promise<void> {
    if (!this.action.can('admin.sla')) return;
    const draft = row ?? this.slaDraft;
    if (!this.slaValid(draft)) { this.action.error.set('ชั่วโมงต้องเป็นค่าบวก และเรียงลำดับ ตอบรับ ≤ เริ่มงาน ≤ แก้ไข'); return; }
    await this.action.run(async () => {
      const r = await firstValueFrom(this.api.saveSlaPolicy(row ?? this.slaDraft, row?.id));
      this.sla = row ? this.sla.map(t => t.id === r.id ? r : t) : [...this.sla, r];
      if (!row) this.slaDraft = this.newSla();
    }, 'บันทึกนโยบาย SLA สำเร็จ');
  }
  protected async saveRule(row?: AssignmentRuleDto): Promise<void> {
    if (!this.action.can('admin.assignment')) return;
    const toCheck = row ?? this.ruleDraft;
    if (!this.ruleValid(toCheck)) { this.action.error.set('ต้องระบุค่าที่ตรงเงื่อนไข และเลือกผู้ใช้หรือทีมอย่างใดอย่างหนึ่ง (ไม่ใช่ทั้งคู่)'); return; }
    await this.action.run(async () => {
      const draft = row ?? this.ruleDraft;
      const r = await firstValueFrom(this.api.saveAssignmentRule({ ...draft, assignToUserId: draft.assignToUserId?.trim() || null, assignToTeam: draft.assignToTeam?.trim() || null }, row?.id));
      this.rules = row ? this.rules.map(t => t.id === r.id ? r : t) : [...this.rules, r];
      if (!row) this.ruleDraft = this.newRule();
    }, 'บันทึกกฎมอบหมายสำเร็จ');
  }
  protected async saveMaster(row?: MasterDataItemDto): Promise<void> {
    if (!this.action.can('admin.masterdata') || this.isMasterReadOnly() || row?.readOnly) return;
    await this.action.run(async () => {
      const draft = row ?? this.masterDraft;
      const input = { code: draft.code, isActive: draft.isActive, ...(row ? { version: row.version } : {}), ...(this.masterType === 'cvss-versions' ? {} : { description: draft.description }) };
      const r = await firstValueFrom(this.api.saveMasterData(this.masterType, input, row?.id ?? undefined));
      this.masterItems = row ? this.masterItems.map(t => t.id === r.id ? r : t) : [...this.masterItems, r];
      if (!row) this.masterDraft = { code: '', description: '', isActive: true };
    }, 'บันทึกข้อมูลหลักสำเร็จ');
  }
}
