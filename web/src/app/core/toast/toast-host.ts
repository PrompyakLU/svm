import { Component, inject } from '@angular/core';
import { SvmIcon } from '../../shared/ui/icon';
import { ToastService } from './toast.service';

/** Renders stacked toasts in a fixed corner region. Mounted once in the shell. */
@Component({
  selector: 'app-toast-host',
  imports: [SvmIcon],
  template: `
    <div class="toasts" aria-live="polite" aria-relevant="additions">
      @for (t of toast.toasts(); track t.id) {
        <div class="toast enter toast--{{ t.kind }}" [attr.role]="t.kind === 'error' ? 'alert' : 'status'">
          <app-icon [name]="icon(t.kind)" [size]="18" />
          <span class="toast__msg">{{ t.message }}</span>
          <button type="button" class="toast__close" (click)="toast.dismiss(t.id)" aria-label="ปิดการแจ้งเตือน">
            <app-icon name="close" [size]="16" />
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toasts { position:fixed; z-index:1000; inset-block-start:16px; inset-inline-end:16px;
      display:flex; flex-direction:column; gap:10px; max-width:min(380px, calc(100vw - 32px));
      pointer-events:none; }
    .toast { pointer-events:auto; display:flex; align-items:flex-start; gap:10px; padding:12px 14px;
      background:var(--color-bg-surface); border:1px solid var(--color-bg-border);
      border-inline-start:4px solid var(--color-bg-border);
      border-radius:var(--radius-md); box-shadow:var(--shadow-md); font-size:var(--fs-body-sm);
      color:var(--color-fg-primary); }
    .toast--success { border-inline-start-color:var(--color-risk-low); color:var(--color-risk-low); }
    .toast--error { border-inline-start-color:var(--color-risk-critical); color:var(--color-risk-critical); }
    .toast--info { border-inline-start-color:var(--color-risk-info); color:var(--color-risk-info); }
    .toast__msg { flex:1 1 auto; min-width:0; color:var(--color-fg-primary); overflow-wrap:anywhere; }
    .toast__close { flex:none; display:inline-flex; align-items:center; justify-content:center;
      width:24px; height:24px; padding:0; color:var(--color-fg-secondary); background:transparent;
      border:0; border-radius:var(--radius-sm); cursor:pointer; }
    .toast__close:hover { background:var(--color-bg-muted); }
    @media (max-width:640px) {
      .toasts { inset-inline:16px; inset-block-start:12px; max-width:none; }
    }
  `],
})
export class ToastHost {
  protected readonly toast = inject(ToastService);
  protected icon(kind: string): string {
    return kind === 'success' ? 'check-circle' : kind === 'error' ? 'alert' : 'info';
  }
}
