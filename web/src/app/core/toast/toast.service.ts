import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  readonly id: number;
  readonly kind: ToastKind;
  readonly message: string;
}

/**
 * Non-intrusive feedback channel. Success/info auto-dismiss so they never block
 * the user; errors stay until dismissed because they may need to be read/acted on.
 * This is the app-wide alternative to a modal "success" dialog (which would
 * interrupt routine flows against UX guidance).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private seq = 0;
  readonly toasts = signal<readonly Toast[]>([]);

  success(message: string): void { this.push('success', message, 4000); }
  info(message: string): void { this.push('info', message, 4000); }
  error(message: string): void { this.push('error', message, 0); }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private push(kind: ToastKind, message: string, autoDismissMs: number): void {
    if (!message) return;
    const id = ++this.seq;
    this.toasts.update((list) => [...list, { id, kind, message }]);
    if (autoDismissMs > 0) {
      setTimeout(() => this.dismiss(id), autoDismissMs);
    }
  }
}
