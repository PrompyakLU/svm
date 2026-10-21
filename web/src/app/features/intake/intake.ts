import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { CreateFindingRequest, ImportResult, SvmApi } from '../../core/api/svm-api';
import { firstValueFrom } from 'rxjs';
import { ActionState, actionError } from '../action-support';
import { DedupCandidate, ImportBatch } from '../../core/models';
import { StateBlock } from '../../shared/ui/ui';
import { SvmIcon } from '../../shared/ui/icon';

type Tab = 'import' | 'manual' | 'dedup' | 'history';

@Component({
  selector: 'app-intake',
  imports: [FormsModule, DatePipe, StateBlock, SvmIcon],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">นำเข้า</p><h1>นำเข้าช่องโหว่</h1></div>
    </header>
    @if (action.error()) { <p class="notice" role="alert">{{ action.error() }}</p> }
    @if (action.success()) { <p class="notice" role="status">{{ action.success() }}</p> }

    <div class="tabs" role="tablist">
      <button role="tab" [attr.aria-selected]="tab()==='import'" class="tab" [class.tab--on]="tab()==='import'" (click)="tab.set('import')">นำเข้าไฟล์</button>
      <button role="tab" [attr.aria-selected]="tab()==='manual'" class="tab" [class.tab--on]="tab()==='manual'" (click)="tab.set('manual')">เพิ่มด้วยตนเอง</button>
      <button role="tab" [attr.aria-selected]="tab()==='dedup'" class="tab" [class.tab--on]="tab()==='dedup'" (click)="tab.set('dedup')">คิวตรวจซ้ำ ({{ dedup().length }})</button>
      <button role="tab" [attr.aria-selected]="tab()==='history'" class="tab" [class.tab--on]="tab()==='history'" (click)="tab.set('history')">ประวัติการนำเข้า</button>
    </div>

    @switch (tab()) {
      @case ('import') {
        <section class="panel">
          <h2 class="panel__title">นำเข้าไฟล์ผลสแกน (CSV / JSON)</h2>
          <p class="notice">ระบบจะตรวจสอบข้อมูลและรายการซ้ำก่อนสร้างช่องโหว่ ผลลัพธ์มาจากการประมวลผลของเซิร์ฟเวอร์</p>

          <div class="dropzone-field">
            <span class="field-label" id="dz-label">เลือกไฟล์</span>
            @if (!fileName()) {
              <label class="dropzone" [class.dropzone--over]="dragOver()"
                [class.dropzone--disabled]="action.busy() || !action.can('finding.import')"
                (dragover)="onDragOver($event)" (dragleave)="onDragLeave()" (drop)="onDrop($event)">
                <input class="dropzone__input" type="file" accept=".csv,.json" aria-labelledby="dz-label"
                  (change)="onFile($event)" [disabled]="action.busy() || !action.can('finding.import')" />
                <app-icon name="cloud" [size]="28" />
                <span class="dropzone__primary">ลากไฟล์มาวาง หรือ <span class="dropzone__browse">เลือกไฟล์</span></span>
                <span class="dropzone__hint">รองรับ .csv และ .json เท่านั้น</span>
              </label>
            } @else {
              <div class="filecard enter">
                <app-icon name="file" [size]="22" />
                <div class="filecard__meta">
                  <span class="filecard__name">{{ fileName() }}</span>
                  <span class="filecard__size">{{ fileSizeLabel() }}</span>
                </div>
                <button type="button" class="icon-btn" (click)="clearFile()" [disabled]="action.busy()" aria-label="ลบไฟล์ที่เลือก">
                  <app-icon name="trash" [size]="18" />
                </button>
              </div>
            }
          </div>

          <div class="field" style="max-width:420px"><label for="transaction-id">รหัสธุรกรรม (ใช้รหัสเดิมเมื่อลองไฟล์เดิมซ้ำ)</label><input id="transaction-id" class="input" [(ngModel)]="transactionId" [disabled]="action.busy()" /></div>

          <div class="import-actions">
            <button type="button" class="btn" (click)="upload()" [disabled]="action.busy() || imported() || !fileName() || !transactionId.trim() || !action.can('finding.import')">
              @if (action.busy()) { <span class="btn-spinner" aria-hidden="true"></span>กำลังประมวลผล... }
              @else { อัปโหลด &amp; ประมวลผล }
            </button>
            <button type="button" class="btn-ghost" (click)="downloadTemplate()" [disabled]="action.busy() || !action.can('finding.import')">ดาวน์โหลดเทมเพลต</button>
          </div>

          @if (result(); as r) {
            <div class="result-card enter" role="status">
              <div class="result-card__head"><app-icon name="check-circle" [size]="18" /><strong>ประมวลผลเสร็จ · {{ r.status }}</strong>@if (r.replayed) { <span class="kev-tag">ผลจากธุรกรรมเดิม</span> }</div>
              <ul class="result-stats">
                <li><span>ทั้งหมด</span><strong class="mono">{{ r.totalRows }}</strong></li>
                <li><span>สร้างใหม่</span><strong class="mono">{{ r.createdCount }}</strong></li>
                <li><span>ซ้ำ</span><strong class="mono">{{ r.duplicateCount }}</strong></li>
                <li><span>รอตัดสิน</span><strong class="mono">{{ r.queuedCount }}</strong></li>
                <li><span>ต้องตรวจทาน</span><strong class="mono">{{ r.needsReviewCount }}</strong></li>
                <li><span>ไม่สำเร็จ</span><strong class="mono">{{ r.failedCount }}</strong></li>
              </ul>
              @if (r.errors.length) {
                <div class="result-errors">@for (e of r.errors; track $index) { <p>แถว {{ e.row }}: {{ e.reason }}</p> }</div>
              }
            </div>
          }
        </section>
      }
      @case ('manual') {
        <section class="panel">
          <h2 class="panel__title">เพิ่มช่องโหว่ด้วยตนเอง</h2>
          <form #manual="ngForm" class="form-grid" (ngSubmit)="submitManual()">
            <div class="field"><label>ชื่อช่องโหว่ *</label><input class="input" [(ngModel)]="m.title" name="title" required /></div>
            <div class="field"><label>Asset Code *</label><input class="input" [(ngModel)]="m.assetCode" name="assetCode" required /></div>
            <div class="field"><label>CVE</label><input class="input" [(ngModel)]="m.cveId" name="cveId" placeholder="CVE-YYYY-NNNN" /></div>
            <div class="field"><label>ตำแหน่ง</label><input class="input" [(ngModel)]="m.location" name="location" /></div>
            <div class="field"><label>ความรุนแรง (CVSS)</label><input class="input" type="number" min="0" max="10" step="0.1" [(ngModel)]="m.cvss" name="cvss" /></div>
            <div class="field"><label>แหล่งที่มา</label><input class="input" [(ngModel)]="m.source" name="source" /></div>
            <div class="field"><label for="manual-severity">ระดับความรุนแรง *</label><select id="manual-severity" class="select" name="severity" [(ngModel)]="severity" required><option>Critical</option><option>High</option><option>Medium</option><option>Low</option><option>Informational</option></select></div>
            <div class="field"><label for="manual-record">รหัสรายการจากแหล่งที่มา</label><input id="manual-record" class="input" name="sourceRecordId" [(ngModel)]="sourceRecordId" /></div>
            <div class="field"><label for="manual-cwe">CWE</label><input id="manual-cwe" class="input" name="cweId" [(ngModel)]="cweId" /></div>
            <div class="field"><label for="manual-version">CVSS version</label><input id="manual-version" class="input" name="cvssVersion" [(ngModel)]="cvssVersion" /></div>
            <div class="field"><label for="manual-vector">CVSS vector</label><input id="manual-vector" class="input" name="cvssVector" [(ngModel)]="cvssVector" /></div>
            <div class="field"><label for="manual-port">Port</label><input id="manual-port" class="input" name="port" type="number" min="0" max="65535" step="1" [(ngModel)]="port" /></div>
            <div class="field"><label for="manual-component">องค์ประกอบ</label><input id="manual-component" class="input" name="component" [(ngModel)]="component" /></div>
            <div class="field"><label for="manual-detected">เวลาตรวจพบ</label><input id="manual-detected" class="input" name="detectedAt" type="datetime-local" [(ngModel)]="detectedAt" /></div>
            <div class="form-actions">
              <button type="submit" class="btn" [disabled]="action.busy() || manual.invalid || !m.title.trim() || !m.assetCode.trim() || !m.source.trim() || !action.can('finding.create')">บันทึก</button>
              @if (manualResult()) { <span class="notice flash">{{ manualResult() }}</span> }
            </div>
          </form>
        </section>
      }
      @case ('dedup') {
        <section class="panel">
          <h2 class="panel__title">คิวตรวจรายการซ้ำ</h2>
          <button class="btn-ghost" (click)="reload()" [disabled]="loading() || action.busy()">โหลดข้อมูลล่าสุด</button>
          <p class="notice">รายการที่ระบบไม่มั่นใจ ต้องให้ผู้ใช้ตัดสิน (รวมกับช่องโหว่เดิม หรือสร้างใหม่)</p>
          @if (loading()) { <app-state-block mode="skeleton" /> }
          @else if (dedup().length === 0) { <app-state-block mode="empty" message="ไม่มีรายการรอตรวจซ้ำ" /> }
          @else {
            <div class="table-wrap enter" style="margin-top:14px"><table class="data">
              <thead><tr><th>รายการใหม่</th><th>Asset</th><th>CVE</th><th>ตรงกับ</th><th>คะแนน</th><th>การตัดสิน</th></tr></thead>
              <tbody>
                @for (d of dedup(); track d.id) {
                  <tr><td>{{ d.findingName }}</td><td class="mono">{{ d.assetCode }}</td><td class="mono">{{ d.cveId || '—' }}</td>
                    <td><select class="select" [attr.aria-label]="'รายการเดิมของ ' + d.findingName" [(ngModel)]="candidate[d.id]" [disabled]="action.busy()"><option value="">เลือกรายการเดิม</option>@for (c of d.candidates ?? []; track c.instanceId) { <option [value]="c.instanceId">{{ c.title }} · {{ c.status }} · {{ c.location }}</option> }</select></td><td class="mono">{{ d.matchScore == null ? '—' : (d.matchScore! * 100).toFixed(0) + '%' }}</td>
                    <td><div class="decision"><input class="input" [attr.aria-label]="'เหตุผลของ ' + d.findingName" [(ngModel)]="dedupReason[d.id]" [disabled]="action.busy()" placeholder="เหตุผลการตัดสิน" /><div class="row-actions"><button class="btn-outline btn-sm" (click)="resolve(d,'merge')" [disabled]="action.busy() || !action.can('finding.dedup') || !candidate[d.id] || !dedupReason[d.id]?.trim()">รวม</button><button class="btn-ghost btn-sm" (click)="resolve(d,'new')" [disabled]="action.busy() || !action.can('finding.dedup') || !dedupReason[d.id]?.trim()">สร้างใหม่</button></div></div></td></tr>
                }
              </tbody></table></div>
          }
          @if (dedupResult()) { <div class="notice flash" style="margin-top:14px">{{ dedupResult() }}</div> }
        </section>
      }
      @case ('history') {
        <section class="panel">
          <h2 class="panel__title">ประวัติการนำเข้า</h2>
          @if (loading()) { <app-state-block mode="skeleton" /> }
          @else {
            <div class="table-wrap enter"><table class="data">
              <thead><tr><th>ไฟล์</th><th>สถานะ</th><th>ทั้งหมด</th><th>สำเร็จ</th><th>ล้มเหลว</th><th>โดย</th><th>เมื่อ</th></tr></thead>
              <tbody>
                @for (b of batches(); track b.id) {
                  <tr><td>{{ b.fileName }}</td><td>{{ b.status }}</td><td class="mono">{{ b.totalRows }}</td>
                    <td class="mono">{{ b.successCount }}</td><td class="mono">{{ b.failedCount }}</td>
                    <td>{{ b.importedBy }}</td><td class="muted">{{ b.importedAt | date:'short' }}</td></tr>
                }
              </tbody></table></div>
          }
        </section>
      }
    }
  `,
  styles: [`
    :host { display:block; }
    .tabs { display:flex; gap:4px; border-bottom:1px solid var(--color-bg-border); margin-bottom:var(--space-md); flex-wrap:wrap; }
    .tab { background:transparent; border:0; border-bottom:2px solid transparent; padding:10px 14px; min-height:44px;
      cursor:pointer; font-size:var(--fs-label); color:var(--color-fg-secondary); }
    .tab--on { color:var(--color-brand-primary); border-bottom-color:var(--color-brand-primary); font-weight:var(--fw-medium); }
    .flash { border-color:var(--color-risk-low); }
    .form-grid { display:grid; grid-template-columns:repeat(2,1fr); gap:16px; }
    .form-actions { grid-column:1/-1; display:flex; align-items:center; gap:14px; }
    @media (max-width:640px){ .form-grid { grid-template-columns:1fr; } }

    .import-actions { display:flex; gap:10px; flex-wrap:wrap; margin-top:4px; }
    .decision { display:flex; flex-direction:column; gap:8px; min-width:200px; }
    .row-actions { display:flex; gap:8px; flex-wrap:wrap; }
  `],
})
export class Intake {
  private readonly api = inject(SvmApi);
  protected readonly tab = signal<Tab>('import');
  protected readonly batches = signal<ImportBatch[]>([]);
  protected readonly dedup = signal<DedupCandidate[]>([]);
  protected readonly loading = signal(true);
  protected readonly action = new ActionState();
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly imported = signal(false);
  private file: File | null = null;
  protected transactionId = crypto.randomUUID();
  protected severity = 'Medium';
  protected sourceRecordId = '';
  protected cweId = '';
  protected cvssVersion = '';
  protected cvssVector = '';
  protected port: number | null = null;
  protected component = '';
  protected detectedAt = '';
  protected candidate: Record<string, string> = {};
  protected dedupReason: Record<string, string> = {};

  protected readonly fileName = signal('');
  protected readonly fileSize = signal(0);
  protected readonly dragOver = signal(false);
  protected readonly manualResult = signal('');
  protected readonly dedupResult = signal('');
  protected m = { title: '', assetCode: '', cveId: '', location: '', cvss: null as number | null, source: 'Manual' };

  constructor() {
    void this.reload();
  }
  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const results = await Promise.allSettled([
        this.action.can('finding.import') ? firstValueFrom(this.api.getImportBatches()) : Promise.resolve([]),
        this.action.can('finding.dedup') ? firstValueFrom(this.api.getDedupQueue()) : Promise.resolve([]),
      ]);
      if (results[0].status === 'fulfilled') this.batches.set(results[0].value); else this.action.error.set(actionError(results[0].reason));
      if (results[1].status === 'fulfilled') this.dedup.set(results[1].value); else this.action.error.set(actionError(results[1].reason));
    } finally { this.loading.set(false); }
  }

  protected onFile(e: Event): void {
    const input = e.target as HTMLInputElement;
    this.setFile(input.files?.[0] ?? null);
  }
  protected onDragOver(e: DragEvent): void {
    if (this.action.busy() || !this.action.can('finding.import')) return;
    e.preventDefault(); this.dragOver.set(true);
  }
  protected onDragLeave(): void { this.dragOver.set(false); }
  protected onDrop(e: DragEvent): void {
    e.preventDefault(); this.dragOver.set(false);
    if (this.action.busy() || !this.action.can('finding.import')) return;
    this.setFile(e.dataTransfer?.files?.[0] ?? null);
  }
  protected clearFile(): void {
    if (this.action.busy()) return;
    this.setFile(null);
  }
  private setFile(file: File | null): void {
    if (file && !/\.(csv|json)$/i.test(file.name)) {
      this.action.error.set('รองรับเฉพาะไฟล์ .csv และ .json');
      return;
    }
    this.file = file;
    this.transactionId = crypto.randomUUID(); this.imported.set(false); this.result.set(null);
    this.fileName.set(file?.name ?? '');
    this.fileSize.set(file?.size ?? 0);
  }
  protected fileSizeLabel(): string {
    const bytes = this.fileSize();
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
  protected async upload(): Promise<void> {
    const file = this.file;
    if (!file || !this.transactionId.trim() || !this.action.can('finding.import') || this.imported()) return;
    await this.action.run(async () => {
      const r = await firstValueFrom(this.api.importFindings(file, this.transactionId.trim()));
      this.result.set(r); this.imported.set(true); await this.reload();
      return `ประมวลผลเสร็จ · Batch: ${r.batchId}`;
    });
  }
  protected async downloadTemplate(): Promise<void> {
    if (!this.action.can('finding.import')) return;
    await this.action.run(async () => {
      const blob = await firstValueFrom(this.api.downloadImportTemplate());
      const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = 'svm-findings-template.csv'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }, 'ดาวน์โหลดเทมเพลตแล้ว');
  }
  protected async submitManual(): Promise<void> {
    if (!this.m.title.trim() || !this.m.assetCode.trim() || !this.m.source.trim() || !this.action.can('finding.create')) return;
    await this.action.run(async () => {
      const input: CreateFindingRequest = { findingName: this.m.title.trim(), assetCode: this.m.assetCode.trim(), sourceSystem: this.m.source.trim(), severity: this.severity, cveId: this.m.cveId, cweId: this.cweId, cvssVersion: this.cvssVersion, cvssVector: this.cvssVector, cvssBaseScore: this.m.cvss, location: this.m.location, port: this.port, component: this.component, sourceRecordId: this.sourceRecordId, detectedAt: this.detectedAt ? new Date(this.detectedAt).toISOString() : null };
      const r = await firstValueFrom(this.api.createFinding(input));
      this.m = { title: '', assetCode: '', cveId: '', location: '', cvss: null, source: 'Manual' };
      this.cweId = ''; this.cvssVersion = ''; this.cvssVector = ''; this.port = null; this.component = ''; this.sourceRecordId = ''; this.detectedAt = '';
      await this.reload();
      return `บันทึกแล้ว · ${r.outcome} · ${r.validationStatus}${r.instanceId ? ' · Instance: ' + r.instanceId : ''}${r.reviewIssues.length ? ' · ' + r.reviewIssues.join(', ') : ''}`;
    });
  }
  protected async resolve(d: DedupCandidate, action: 'merge' | 'new'): Promise<void> {
    const reason = this.dedupReason[d.id]?.trim(); const instanceId = this.candidate[d.id];
    if (!this.action.can('finding.dedup') || !reason || (action === 'merge' && !d.candidates?.some(c => c.instanceId === instanceId))) return;
    await this.action.run(async () => {
      const r = await firstValueFrom(this.api.resolveDuplicate(d.findingId ?? d.id, { decision: action === 'merge' ? 'Duplicate' : 'Distinct', reason, ...(action === 'merge' ? { instanceId } : {}) }));
      await this.reload(); return `บันทึกการตัดสินแล้ว · ${r.decision} · Instance: ${r.instanceId}`;
    });
  }
}
