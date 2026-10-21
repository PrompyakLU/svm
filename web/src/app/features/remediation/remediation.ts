import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { DatePipe } from '@angular/common';
import { RemediationTaskResult, SvmApi } from '../../core/api/svm-api';
import { VulnerabilityInstance, VulnStatus } from '../../core/models';
import { ActionState, actionError } from '../action-support';
import { RiskBadge, StateBlock, StatusBadge } from '../../shared/ui/ui';

@Component({
  selector: 'app-remediation',
  imports: [FormsModule, RouterLink, DatePipe, RiskBadge, StateBlock, StatusBadge],
  template: `
    <header class="page-head"><div><p class="eyebrow">การแก้ไข</p><h1>งานแก้ไข (Remediation)</h1></div></header>
    <p class="notice">รายการช่องโหว่ที่อยู่ระหว่างมอบหมาย/แก้ไข พร้อมความคืบหน้าของงาน</p>
    @if (action.error()) { <p class="notice" role="alert">{{ action.error() }}</p> }
    @if (action.success()) { <p class="notice" role="status">{{ action.success() }}</p> }
    <div class="controls"><label for="remediation-status">สถานะ</label><select id="remediation-status" class="select" [(ngModel)]="status" (ngModelChange)="page = 1; load()" [disabled]="loading() || action.busy()"><option value="Assigned">มอบหมายแล้ว</option><option value="InProgress">กำลังแก้ไข</option><option value="Confirmed">ยืนยันแล้ว / รอมอบหมาย</option><option value="Reopened">เปิดงานใหม่</option><option value="PendingVerification">รอตรวจสอบ</option></select><button class="btn-ghost" (click)="load()" [disabled]="loading() || action.busy()">โหลดข้อมูลล่าสุด</button></div>
    @if (task; as t) {
      <section class="panel"><h2 class="panel__title">แผนและความคืบหน้า · {{ selectedCode }}</h2><p class="muted">กำหนด SLA: {{ t.dueDate | date:'mediumDate' }}</p>
        <form #edit="ngForm" class="edit-grid" (ngSubmit)="saveTask()"><fieldset [disabled]="action.busy() || !action.can('task.update')" class="edit-grid">
          <div class="field"><label for="rem-root">สาเหตุ</label><textarea id="rem-root" class="textarea" name="rootCause" [(ngModel)]="draft.rootCause"></textarea></div>
          <div class="field"><label for="rem-plan">แผนแก้ไข</label><textarea id="rem-plan" class="textarea" name="actionPlan" [(ngModel)]="draft.actionPlan"></textarea></div>
          <div class="field"><label for="rem-target">วันที่เป้าหมาย</label><input id="rem-target" class="input" name="targetDate" type="date" [(ngModel)]="draft.targetDate" /></div>
          <div class="field"><label for="rem-ref">Change reference</label><input id="rem-ref" class="input" name="changeReference" [(ngModel)]="draft.changeReference" /></div>
          <div class="field"><label for="rem-progress">ความคืบหน้า (%)</label><input id="rem-progress" class="input" name="progress" type="number" min="0" max="100" step="1" required [(ngModel)]="draft.progressPercent" /></div>
          <div class="field"><label for="rem-status">สถานะงาน</label><select id="rem-status" class="select" name="taskStatus" [(ngModel)]="draft.status"><option>Open</option><option>InProgress</option><option>Blocked</option><option>Completed</option></select></div>
          <div class="field"><label for="rem-obstacle">อุปสรรค (จำเป็นเมื่อ Blocked)</label><textarea id="rem-obstacle" class="textarea" name="obstacle" [(ngModel)]="draft.obstacle" [required]="draft.status === 'Blocked'"></textarea></div>
          <div class="field"><label for="rem-comment">ความคิดเห็นประกอบ</label><textarea id="rem-comment" class="textarea" name="comment" [(ngModel)]="draft.comment"></textarea></div>
          <button class="btn" [disabled]="edit.invalid || (draft.status === 'Completed' && draft.progressPercent !== 100) || (draft.status !== 'Completed' && draft.progressPercent === 100)">บันทึกแผนและความคืบหน้า</button>
        </fieldset></form>
        @if (t.targetDateExceedsSla) { <p class="notice">วันที่เป้าหมายเกิน SLA กรุณาตรวจทานแผน</p> }
        <div class="controls"><a [routerLink]="['/vulnerabilities', t.instanceId]">หลักฐาน ความคิดเห็น และส่งตรวจสอบ</a><button class="btn-ghost" (click)="refreshTask()" [disabled]="action.busy()">โหลดงานล่าสุด (แทนที่ค่าที่กำลังแก้ไข)</button><button class="btn-ghost" (click)="task = null" [disabled]="action.busy()">ปิดแผน</button></div>
      </section>
    }

    @if (loading()) { <app-state-block mode="skeleton" /> }
    @else if (error()) { <app-state-block mode="error" [message]="error()" /> }
    @else if (rows().length === 0) { <app-state-block mode="empty" message="ไม่มีงานแก้ไขที่กำลังดำเนินการ" /> }
    @else {
      <div class="table-wrap enter" style="margin-top:14px"><table class="data">
        <thead><tr><th>รหัส</th><th>ช่องโหว่</th><th>ความเสี่ยง</th><th>สถานะ</th><th>ผู้รับผิดชอบ</th><th>กำหนดเสร็จ</th><th>การดำเนินการ</th></tr></thead>
        <tbody>
          @for (v of rows(); track v.id) {
            <tr>
              <td><a class="mono" [routerLink]="['/vulnerabilities', v.id]">{{ v.code }}</a></td>
              <td>{{ v.title }}</td>
              <td><app-risk-badge [level]="v.riskLevel" /></td>
              <td><app-status-badge [status]="v.status" /></td>
              <td>{{ v.remediation?.assignedTo || v.assignedTo || '—' }}</td>
              <td class="muted">{{ v.sla.dueDate ? (v.sla.dueDate | date:'mediumDate') : '—' }}</td>
              <td style="min-width:150px"><div class="act-cell">
                @if (action.can('task.read')) { <button class="btn-outline btn-sm" (click)="openTask(v)" [disabled]="action.busy()">แผนและความคืบหน้า</button> }
                <a class="btn-link" [routerLink]="['/vulnerabilities', v.id]">มอบหมาย / หลักฐาน / ส่งตรวจ</a>
              </div></td>
            </tr>
          }
        </tbody></table></div>
    }
    @if (!loading() && !error() && rows().length) {
      <div class="pager">
        <span class="muted">แสดง {{ rangeStart() }}–{{ rangeEnd() }} จาก {{ total() }} รายการ</span>
        <div class="pager__ctrls">
          <button type="button" class="btn-outline" (click)="prev()" [disabled]="page <= 1 || loading() || action.busy()">← ก่อนหน้า</button>
          <span class="pager__page">หน้า {{ page }} / {{ totalPages() }}</span>
          <button type="button" class="btn-outline" (click)="next()" [disabled]="page >= totalPages() || loading() || action.busy()">ถัดไป →</button>
        </div>
      </div>
    }
  `,
  styles: [`
    :host{display:block}
    .controls{display:flex;gap:var(--space-sm);align-items:center;flex-wrap:wrap;margin-block:var(--space-sm)}.edit-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:var(--space-sm)}form.edit-grid{display:block}fieldset{margin:0;padding:0;border:0;min-width:0}.field{min-width:0}textarea{width:100%}@media(max-width:640px){.edit-grid{grid-template-columns:1fr}}
    .pager{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-top:var(--space-sm)}
    .pager__ctrls{display:flex;align-items:center;gap:12px}
    .pager__page{font-size:var(--fs-label);font-variant-numeric:tabular-nums}
    .act-cell{display:flex;flex-direction:column;gap:6px;align-items:flex-start}
  `],
})
export class Remediation {
  private readonly api = inject(SvmApi);
  private readonly router = inject(Router);
  protected readonly rows = signal<VulnerabilityInstance[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly action = new ActionState();
  protected readonly total = signal(0);
  protected status: VulnStatus = 'Assigned';
  protected page = 1;
  protected readonly pageSize = 25;
  protected task: RemediationTaskResult | null = null;
  protected selectedCode = '';
  protected draft = { rootCause: '', actionPlan: '', targetDate: '', changeReference: '', progressPercent: 0, status: 'Open', obstacle: '', comment: '' };

  constructor() {
    void this.load();
  }
  protected async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try { const r = await firstValueFrom(this.api.getVulnerabilities({ status: this.status, page: this.page, pageSize: this.pageSize })); this.rows.set(r.items); this.total.set(r.total); }
    catch (error) { this.error.set(actionError(error)); }
    finally { this.loading.set(false); }
  }
  protected totalPages(): number { return Math.max(1, Math.ceil(this.total() / this.pageSize)); }
  protected rangeStart(): number { return this.total() > 0 ? (this.page - 1) * this.pageSize + 1 : 0; }
  protected rangeEnd(): number { return Math.min(this.page * this.pageSize, this.total()); }
  protected prev(): void { if (this.page > 1) { this.page--; void this.load(); } }
  protected next(): void { if (this.page < this.totalPages()) { this.page++; void this.load(); } }
  private setTask(task: RemediationTaskResult): void {
    this.task = task;
    this.draft = { rootCause: task.rootCause ?? '', actionPlan: task.actionPlan ?? '', targetDate: task.targetDate?.slice(0, 10) ?? '', changeReference: task.changeReference ?? '', progressPercent: task.progressPercent, status: task.status, obstacle: task.obstacle ?? '', comment: '' };
  }
  protected async openTask(v: VulnerabilityInstance): Promise<void> {
    if (!this.action.can('task.read')) return;
    await this.action.run(async () => {
      const fresh = await firstValueFrom(this.api.getVulnerability(v.id));
      if (!fresh?.remediation) throw new Error('ยังไม่มีงานแก้ไข กรุณามอบหมายจากหน้ารายละเอียดก่อน');
      this.setTask(await firstValueFrom(this.api.getRemediationTask(fresh.remediation.id))); this.selectedCode = fresh.code;
    }, 'โหลดงานล่าสุดแล้ว');
  }
  protected async refreshTask(): Promise<void> {
    const id = this.task?.id;
    if (!id || !this.action.can('task.read')) return;
    await this.action.run(async () => { this.setTask(await firstValueFrom(this.api.getRemediationTask(id))); }, 'โหลดงานล่าสุดแล้ว');
  }
  protected async saveTask(): Promise<void> {
    const task = this.task;
    if (!task || !this.action.can('task.update')) return;
    await this.action.run(async () => {
      this.setTask(await firstValueFrom(this.api.updateRemediationTask(task.id, { ...this.draft, targetDate: this.draft.targetDate ? this.draft.targetDate + 'T00:00:00' : undefined, rowVersion: task.rowVersion })));
      await this.load();
    }, 'บันทึกแผนและความคืบหน้าสำเร็จ');
  }

  protected go(id: string): void { this.router.navigate(['/vulnerabilities', id]); }
}
