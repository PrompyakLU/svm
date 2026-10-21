import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  readonly title: string;
  readonly message: string;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
  /** Marks the action as destructive so the confirm button uses the danger style. */
  readonly danger?: boolean;
}

interface PendingConfirm extends ConfirmOptions {
  readonly resolve: (ok: boolean) => void;
}

/**
 * Modal confirmation for consequential/irreversible actions only (delete, close,
 * reopen). Routine success is handled by non-intrusive toasts, not a dialog.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly pending = signal<PendingConfirm | null>(null);

  ask(options: ConfirmOptions): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.pending.set({ ...options, resolve });
    });
  }

  settle(ok: boolean): void {
    const current = this.pending();
    if (!current) return;
    this.pending.set(null);
    current.resolve(ok);
  }
}
