import { HttpErrorResponse } from '@angular/common/http';
import { inject, signal } from '@angular/core';
import { AuthService } from '../core/auth/auth.service';
import { ToastService } from '../core/toast/toast.service';

export class ActionState {
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly success = signal('');
  can(permission: string): boolean { return this.auth.user()?.permissions.includes(permission) ?? false; }
  async run(work: () => Promise<string | void>, message = 'บันทึกสำเร็จ'): Promise<boolean> {
    if (this.busy()) return false;
    this.busy.set(true); this.error.set(''); this.success.set('');
    try {
      const result = (await work()) || message;
      this.success.set(result);
      this.toast.success(result);
      return true;
    }
    catch (error) {
      const text = actionError(error);
      this.error.set(text);
      this.toast.error(text);
      return false;
    }
    finally { this.busy.set(false); }
  }
}
export function actionError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return error instanceof Error ? error.message : 'ดำเนินการไม่สำเร็จ กรุณาลองอีกครั้ง';
  const problem = error.error;
  const detail = problem && typeof problem === 'object' ? problem : {};
  const fields = detail.errors && typeof detail.errors === 'object'
    ? Object.entries(detail.errors).map(([field, messages]) => `${field}: ${Array.isArray(messages) ? messages.join(', ') : String(messages)}`).join(' · ') : '';
  const prefix = error.status === 409
    ? 'ข้อมูลเปลี่ยนโดยผู้ใช้อื่น กรุณาโหลดข้อมูลล่าสุด ตรวจทาน แล้วส่งใหม่'
    : error.status === 403 ? 'คุณไม่มีสิทธิ์ดำเนินการนี้'
    : error.status === 0 ? 'เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ กรุณาตรวจสอบเครือข่าย'
    : typeof detail.detail === 'string' ? detail.detail : 'ดำเนินการไม่สำเร็จ';
  return [prefix, detail.code, fields, detail.requestId ? `Request: ${detail.requestId}` : ''].filter(Boolean).join(' · ');
}

