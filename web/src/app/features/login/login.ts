import { Component, DestroyRef, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { safeReturnUrl } from '../../core/auth/auth.guard';
import { SvmIcon } from '../../shared/ui/icon';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, SvmIcon],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  protected readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private focusErrorOnRender = false;

  @ViewChild('authError') set authError(element: ElementRef<HTMLElement> | undefined) {
    if (element && this.focusErrorOnRender) {
      element.nativeElement.focus();
      this.focusErrorOnRender = false;
    }
  }

  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  protected readonly submitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly sessionExpired = this.route.snapshot.queryParamMap.get('reason') === 'expired';

  protected submit(): void {
    this.submitted.set(true);
    this.errorMessage.set(null);
    if (this.loading()) return;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    const { username, password } = this.form.getRawValue();
    this.auth.login(username.trim(), password).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: () => {
        void this.router.navigateByUrl(
          safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')),
        );
      },
      error: (error: unknown) => {
        const suppliedMessage = error instanceof Error ? error.message.trim() : '';
        this.focusErrorOnRender = true;
        this.errorMessage.set(suppliedMessage || 'ไม่สามารถเข้าสู่ระบบได้ กรุณาลองอีกครั้ง');
      },
    });
  }

  protected clearError(): void {
    this.errorMessage.set(null);
  }

  protected togglePassword(): void {
    this.showPassword.update((visible) => !visible);
  }
}
