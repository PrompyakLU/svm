import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ActionState, actionError } from '../action-support';
import { SvmApi } from '../../core/api/svm-api';
import { ConfirmService } from '../../core/dialog/confirm.service';
import { VulnerabilityInstance, VulnStatus } from '../../core/models';
import { RiskBadge, StateBlock, StatusBadge } from '../../shared/ui/ui';
import { RISK_LABEL, STATUS_LABEL } from '../../shared/labels';

type VerificationAction = 'submit' | 'rescan' | 'record';

@Component({
  selector: 'app-verification',
  imports: [FormsModule, RouterLink, RiskBadge, StateBlock, StatusBadge],
  template: `
    <header class="page-head"><div><p class="eyebrow">ตรวจสอบ</p><h1>ตรวจสอบผลการแก้ไข (Verification)</h1></div></header>
    <p class="notice">ช่องโหว่ที่แก้ไขแล้วและรอตรวจสอบ — ส่งตรวจ, ขอ rescan, หรือบันทึกผล (Critical/High ต้องผู้ตรวจอิสระ)</p>
    @if (actionState.error()) { <p class="notice" role="alert">{{ actionState.error() }}</p> }
    @if (actionState.success()) { <p class="notice" role="status">{{ actionState.success() }}</p> }
    <div class="act"><label for="verification-status">สถานะ</label><select id="verification-status" class="select" [(ngModel)]="status" (ngModelChange)="changeStatus()" [disabled]="actionState.busy() || loading()"><option value="PendingVerification">รอตรวจสอบ</option><option value="InProgress">กำลังแก้ไข / ส่งตรวจ</option><option value="Resolved">แก้ไขแล้ว / ขอ Rescan</option><option value="Closed">ปิดแล้ว / ขอ Rescan</option></select><button class="btn-ghost" (click)="load()" [disabled]="actionState.busy() || loading()">โหลดข้อมูลล่าสุด</button></div>
    @if (selected(); as v) {
      <section class="panel" aria-label="ดำเนินการตรวจสอบ"><h2 class="panel__title">{{ v.code }} — {{ v.title }}</h2><p>{{ statusLabel[v.status] }} · {{ riskLabel[v.riskLevel] }} · หลักฐาน {{ v.evidence.length }} รายการ</p>
        @for (e of v.evidence; track e.id) { <p class="muted">{{ e.fileName }} · {{ e.scanResult }}</p> }
        <form (ngSubmit)="perform()"><div class="field"><label for="verify-notes">บันทึกประกอบ</label><textarea id="verify-notes" class="textarea" name="notes" maxlength="2000" [(ngModel)]="notes" [disabled]="actionState.busy()"></textarea></div>
          @if (kind === 'record') {
            <div class="field"><label for="verify-result">ผลตรวจสอบ</label><select id="verify-result" class="select" name="result" [(ngModel)]="result" [disabled]="actionState.busy()"><option value="Verified">แก้ไขแล้ว</option><option value="NotResolved">ยังไม่แก้ไข</option><option value="PartiallyResolved">แก้ไขบางส่วน</option></select></div>
            <div class="field"><label for="verify-method">วิธีตรวจสอบ</label><select id="verify-method" class="select" name="method" [(ngModel)]="method" [disabled]="actionState.busy()"><option>Manual</option><option>Rescan</option></select></div>
          }
          <div class="act"><button class="btn" [disabled]="actionState.busy() || !v.rowVersion || !actionState.can('verification.' + kind)">{{ kind === 'submit' ? 'ยืนยันส่งตรวจ' : kind === 'rescan' ? 'ยืนยันขอ Rescan' : 'บันทึกผลตรวจสอบ' }}</button><button class="btn-ghost" type="button" (click)="selected.set(null)" [disabled]="actionState.busy()">ยกเลิก</button><a [routerLink]="['/vulnerabilities', v.id]">เปิดรายละเอียดและหลักฐาน</a></div>
        </form>
      </section>
    }

    @if (loading()) { <app-state-block mode="skeleton" /> }
    @else if (error()) { <app-state-block mode="error" [message]="error()" /> }
    @else if (rows().length === 0) { <app-state-block mode="empty" message="ไม่มีรายการรอตรวจสอบ" /> }
    @else {
      <div class="table-wrap enter"><table class="data">
        <thead><tr><th>รหัส</th><th>ช่องโหว่</th><th>ความเสี่ยง</th><th>สถานะ</th><th>การตรวจสอบ</th><th>การดำเนินการ</th></tr></thead>
        <tbody>
          @for (v of rows(); track v.id) {
            <tr>
              <td><a class="mono" [routerLink]="['/vulnerabilities', v.id]">{{ v.code }}</a></td>
              <td>{{ v.title }}</td>
              <td><app-risk-badge [level]="v.riskLevel" /></td>
              <td><app-status-badge [status]="v.status" /></td>
              <td class="muted">เปิดรายละเอียดเพื่อดูผลและหลักฐานล่าสุด</td>
              <td class="act">
                @if (v.status === 'InProgress' && actionState.can('verification.submit')) { <button class="btn-ghost btn-sm" (click)="prepare(v,'submit')" [disabled]="actionState.busy()">ส่งตรวจ</button> }
                @if (v.status !== 'InProgress' && actionState.can('verification.rescan')) { <button class="btn-ghost btn-sm" (click)="prepare(v,'rescan')" [disabled]="actionState.busy()">ขอ Rescan</button> }
                @if (v.status === 'PendingVerification' && actionState.can('verification.record')) { <button class="btn-ghost btn-sm" (click)="prepare(v,'record')" [disabled]="actionState.busy()">บันทึกผล</button> }
              </td>
            </tr>
          }
        </tbody></table></div>
    }
    <div class="act"><button class="btn-ghost" (click)="page = page - 1; load()" [disabled]="page <= 1 || loading() || actionState.busy()">ก่อนหน้า</button><span>หน้า {{ page }} · {{ total() }} รายการ</span><button class="btn-ghost" (click)="page = page + 1; load()" [disabled]="page * 25 >= total() || loading() || actionState.busy()">ถัดไป</button></div>
  `,
  styles: [`
    :host{display:block}
    .link{cursor:pointer;text-decoration:underline}
    .act{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
    .flash{border-color:var(--color-risk-low)}
  `],
})
export class Verification {
  private readonly api = inject(SvmApi);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly riskLabel = RISK_LABEL;
  protected readonly rows = signal<VulnerabilityInstance[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly actionState = new ActionState();
  protected readonly selected = signal<VulnerabilityInstance | null>(null);
  protected readonly total = signal(0);
  protected status: VulnStatus = 'PendingVerification';
  protected page = 1;
  protected kind: VerificationAction = 'record';
  protected result = 'Verified';
  protected method = 'Manual';
  protected notes = '';

  constructor() {
    void this.load();
  }
  protected changeStatus(): void { this.page = 1; this.selected.set(null); void this.load(); }
  protected async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try { const r = await firstValueFrom(this.api.getVulnerabilities({ status: this.status, page: this.page, pageSize: 25 })); this.rows.set(r.items); this.total.set(r.total); }
    catch (error) { this.error.set(actionError(error)); }
    finally { this.loading.set(false); }
  }

  protected go(id: string): void { this.router.navigate(['/vulnerabilities', id]); }

  protected async prepare(v: VulnerabilityInstance, kind: VerificationAction): Promise<void> {
    if (!this.actionState.can('verification.' + kind)) return;
    await this.actionState.run(async () => {
      const fresh = await firstValueFrom(this.api.getVulnerability(v.id));
      if (!fresh?.rowVersion) throw new Error('ไม่พบข้อมูลล่าสุดหรือเวอร์ชันข้อมูล กรุณาโหลดรายการใหม่');
      if ((kind === 'submit' && fresh.status !== 'InProgress') || (kind === 'record' && fresh.status !== 'PendingVerification') || (kind === 'rescan' && !['PendingVerification', 'Resolved', 'Closed'].includes(fresh.status))) throw new Error('สถานะเปลี่ยนแล้ว กรุณาโหลดรายการใหม่');
      this.kind = kind; this.selected.set(fresh); this.notes = ''; this.result = 'Verified'; this.method = 'Manual';
    }, 'โหลดรายละเอียดล่าสุดแล้ว กรุณาตรวจทานก่อนยืนยัน');
  }
  protected async perform(): Promise<void> {
    const v = this.selected();
    if (!v?.rowVersion || !this.actionState.can('verification.' + this.kind)) return;
    const prompts: Record<VerificationAction, { title: string; message: string }> = {
      submit: { title: 'ส่งตรวจสอบ', message: `ส่ง ${v.code} เข้ากระบวนการตรวจสอบใช่หรือไม่` },
      rescan: { title: 'ขอ Rescan', message: `ขอสแกนซ้ำสำหรับ ${v.code} ใช่หรือไม่ การดำเนินการนี้อาจเปิดงานที่ปิดไปแล้วใหม่` },
      record: { title: 'บันทึกผลการตรวจสอบ', message: `บันทึกผล "${this.result}" ให้ ${v.code} ใช่หรือไม่ ผลนี้จะเปลี่ยนสถานะของงานและย้อนกลับไม่ได้` },
    };
    const ok = await this.confirm.ask({ ...prompts[this.kind], confirmLabel: 'ยืนยัน', danger: this.kind === 'record' });
    if (!ok) return;
    await this.actionState.run(async () => {
      let message: string;
      if (this.kind === 'submit') { await firstValueFrom(this.api.submitVerification(v.id, { rowVersion: v.rowVersion!, notes: this.notes.trim() })); message = 'ส่งตรวจสอบแล้ว'; }
      else if (this.kind === 'rescan') { const r = await firstValueFrom(this.api.rescanInstance(v.id)); message = `Rescan: ${r.status} · Request: ${r.requestId}${r.reopened ? ' · เปิดงานใหม่แล้ว' : ''}`; }
      else { const r = await firstValueFrom(this.api.recordVerification(v.id, { rowVersion: v.rowVersion!, result: this.result, method: this.method, notes: this.notes.trim() })); message = `บันทึกผลแล้ว · ${r.result} · ${r.toStatus}`; }
      this.selected.set(null); await this.load(); return message;
    });
  }
}
