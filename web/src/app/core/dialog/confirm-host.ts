import { AfterViewChecked, Component, ElementRef, ViewChild, inject } from '@angular/core';
import { ConfirmService } from './confirm.service';

/** Renders the active confirm dialog using a native <dialog> for focus trapping. */
@Component({
  selector: 'app-confirm-host',
  template: `
    <dialog #dlg class="confirm" (cancel)="onCancel($event)" (click)="onBackdrop($event)">
      @if (confirm.pending(); as p) {
        <div class="confirm__body">
          <h2 class="confirm__title" id="confirm-title">{{ p.title }}</h2>
          <p class="confirm__msg">{{ p.message }}</p>
          <div class="confirm__actions">
            <button type="button" class="btn-outline" (click)="confirm.settle(false)">
              {{ p.cancelLabel || 'ยกเลิก' }}
            </button>
            <button type="button" [class]="p.danger ? 'btn btn--danger' : 'btn'" autofocus (click)="confirm.settle(true)">
              {{ p.confirmLabel || 'ยืนยัน' }}
            </button>
          </div>
        </div>
      }
    </dialog>
  `,
  styles: [`
    .confirm { padding:0; border:0; border-radius:var(--radius-lg); max-width:420px; width:calc(100vw - 32px);
      background:var(--color-bg-surface); color:var(--color-fg-primary); box-shadow:var(--shadow-lg); }
    .confirm::backdrop { background:var(--color-overlay-backdrop); }
    .confirm__body { padding:20px; display:flex; flex-direction:column; gap:12px; }
    .confirm__title { margin:0; font-size:var(--fs-h3); line-height:var(--lh-h3); font-weight:var(--fw-bold); }
    .confirm__msg { margin:0; color:var(--color-fg-secondary); line-height:var(--lh-body); }
    .confirm__actions { display:flex; justify-content:flex-end; gap:10px; margin-top:4px; }
  `],
})
export class ConfirmHost implements AfterViewChecked {
  protected readonly confirm = inject(ConfirmService);
  @ViewChild('dlg') private dlg?: ElementRef<HTMLDialogElement>;

  ngAfterViewChecked(): void {
    const dialog = this.dlg?.nativeElement;
    if (!dialog) return;
    const shouldOpen = this.confirm.pending() !== null;
    if (shouldOpen && !dialog.open) dialog.showModal();
    else if (!shouldOpen && dialog.open) dialog.close();
  }

  protected onCancel(event: Event): void {
    event.preventDefault();
    this.confirm.settle(false);
  }

  protected onBackdrop(event: MouseEvent): void {
    if (event.target === this.dlg?.nativeElement) this.confirm.settle(false);
  }
}
