import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { SvmApi } from '../../core/api/svm-api';
import { AuditEntry } from '../../core/models';
import { StateBlock } from '../../shared/ui/ui';

@Component({
  selector: 'app-audit',
  imports: [FormsModule, DatePipe, StateBlock],
  template: `
    <header class="page-head"><div><p class="eyebrow">ตรวจสอบย้อนหลัง</p><h1>บันทึกการตรวจสอบ (Audit Trail)</h1></div></header>
    <p class="notice">บันทึกแบบ append-only ที่ป้องกันการแก้ไข (hash-chained) — ดูการกระทำสำคัญทั้งหมดในระบบ</p>

    <div class="toolbar" style="margin-top:14px">
      <div class="field grow" style="flex:1 1 280px">
        <label for="q">ค้นหา</label>
        <input id="q" class="input" type="search" placeholder="ผู้กระทำ, การกระทำ, วัตถุ, ผลลัพธ์" [(ngModel)]="search" (ngModelChange)="onSearch()" />
      </div>
    </div>

    @if (loading()) { <app-state-block mode="skeleton" [rows]="6" /> }
    @else if (error()) { <app-state-block mode="error" /> }
    @else if (rows().length === 0) { <app-state-block mode="empty" message="ไม่พบบันทึก" /> }
    @else {
      <div class="table-wrap enter"><table class="data">
        <thead><tr><th>เวลา</th><th>ผู้กระทำ</th><th>การกระทำ</th><th>วัตถุ</th><th>ผลลัพธ์</th><th>Request Id</th></tr></thead>
        <tbody>
          @for (a of rows(); track a.id) {
            <tr><td class="muted">{{ a.timestamp | date:'medium' }}</td><td>{{ a.actor }}</td>
              <td class="mono">{{ a.action }}</td><td>{{ a.objectType }} <span class="muted mono">{{ a.objectId }}</span></td>
              <td>{{ a.result }}</td><td class="muted mono">{{ a.requestId }}</td></tr>
          }
        </tbody></table></div>
    }
  `,
  styles: [`:host{display:block}`],
})
export class Audit {
  private readonly api = inject(SvmApi);
  protected readonly rows = signal<AuditEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected search = '';
  private t?: ReturnType<typeof setTimeout>;

  constructor() { this.load(); }

  private load(): void {
    this.loading.set(true); this.error.set(false);
    this.api.getAudit(this.search).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); },
      error: () => { this.error.set(true); this.loading.set(false); },
    });
  }
  protected onSearch(): void { clearTimeout(this.t); this.t = setTimeout(() => this.load(), 250); }
}
