import { Component } from '@angular/core';

/** Task terminology stays available to keyboard and touch users at every viewport. */
@Component({
  selector: 'app-dashboard-help',
  template: `
<details class="queue-help">
    <summary>คำอธิบาย KEV, SLA และคะแนนความเสี่ยง</summary>
    <dl>
      <div><dt>KEV</dt><dd>ช่องโหว่ที่มีรายงานการนำไปใช้โจมตีจริง คิวนี้แสดงเฉพาะรายการที่ยังเปิดอยู่</dd></div>
      <div><dt>SLA</dt><dd>กำหนดเวลาที่ต้องแก้ไขช่องโหว่ จำนวนวันที่เหลือหรือเกินกำหนดอ้างอิงค่าที่บันทึกไว้ ไม่ได้คำนวณจากวันนี้</dd></div>
      <div><dt>คะแนนความเสี่ยง 0–100</dt><dd>ยิ่งสูงยิ่งเสี่ยง เป็นคะแนนรวมจากปัจจัยทางเทคนิคและธุรกิจ เปิดงานเพื่อดูรายละเอียดประกอบคะแนน</dd></div>
    </dl>
  </details>
  `,
  styles: [`
.queue-help { margin-bottom: var(--space-sm); color: var(--color-fg-secondary); font-size: var(--fs-body-sm); }
.queue-help summary { width: fit-content; max-width: 100%; min-height: 44px; padding-block: 10px; cursor: pointer; color: var(--color-brand-primary); text-underline-offset: 3px; }
.queue-help summary:hover { text-decoration: underline; }
.queue-help summary:focus-visible { outline: 2px solid var(--color-brand-primary); outline-offset: 2px; border-radius: var(--radius-sm); }
.queue-help dl { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--space-md); margin: 0 0 var(--space-md); padding: var(--space-md); background: var(--color-bg-surface-raised); border-radius: var(--radius-sm); }
.queue-help dt { color: var(--color-fg-primary); font-weight: var(--fw-medium); }
.queue-help dd { margin: var(--space-xs) 0 0; max-width: 65ch; }
    @media (max-width: 600px) { .queue-help dl { grid-template-columns: minmax(0, 1fr); } }
  `],
})
export class DashboardHelp {}
