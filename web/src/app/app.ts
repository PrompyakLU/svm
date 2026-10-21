import { Component, DestroyRef, ElementRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { SvmIcon } from './shared/ui/icon';
import { ToastHost } from './core/toast/toast-host';
import { ConfirmHost } from './core/dialog/confirm-host';
import { AuthService } from './core/auth/auth.service';
import { ThemeService } from './core/theme/theme.service';
import { hasAnyPermission, safeReturnUrl } from './core/auth/auth.guard';
import { environment } from '../environments/environment';

interface NavItem {
  readonly label: string;
  readonly path: string;
  readonly icon: string;
  readonly section: 'primary' | 'management';
  readonly permission?: string;
}

interface Breadcrumb {
  readonly label: string;
  readonly path?: string;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SvmIcon, ToastHost, ConfirmHost],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);

  @ViewChild('mobileDialog') private mobileDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('mobileMenuButton') private mobileMenuButton?: ElementRef<HTMLButtonElement>;

  protected readonly appName = 'SVM';
  protected readonly sidebarCollapsed = signal(false);
  protected readonly menuOpen = signal(false);
  protected readonly currentUrl = signal(this.router.url);
  protected readonly showShell = computed(() => this.auth.user() !== null && this.currentUrl().split(/[?#]/, 1)[0] !== '/login');
  protected readonly searchValue = signal(this.router.parseUrl(this.router.url).queryParams['q'] ?? '');

  private readonly allNavItems: readonly NavItem[] = [
    { label: 'แดชบอร์ด', path: '/dashboard', icon: 'dashboard', section: 'primary' },
    { label: 'ช่องโหว่', path: '/vulnerabilities', icon: 'shield', section: 'primary' },
    { label: 'นำเข้า', path: '/intake', icon: 'upload', section: 'primary' },
    { label: 'การแก้ไข', path: '/remediation', icon: 'wrench', section: 'primary' },
    { label: 'ตรวจสอบ', path: '/verification', icon: 'check', section: 'primary' },
    { label: 'ผู้ดูแลระบบ', path: '/admin', icon: 'settings', section: 'management' },
    { label: 'บันทึกตรวจสอบ', path: '/audit', icon: 'audit', section: 'management' },
  ];
  protected readonly navItems = computed(() => {
    const permissions = this.auth.user()?.permissions ?? [];
    return this.allNavItems.filter(item => {
      const required = item.permission ? [item.permission] : ({
        '/dashboard': ['dashboard.ops', 'dashboard.exec'], '/vulnerabilities': ['instance.read'],
        '/intake': ['finding.import', 'finding.create', 'finding.dedup'],
        '/remediation': ['instance.read', 'task.read'],
        '/verification': ['verification.submit', 'verification.rescan', 'verification.record'],
        '/admin': ['admin.masterdata', 'admin.workflow', 'admin.sla', 'admin.risk', 'admin.assignment', 'admin.users'],
        '/audit': ['audit.read'],
      } as Record<string, string[]>)[item.path] ?? [];
      return hasAnyPermission(permissions, required);
    });
  });
  protected readonly workshopData = environment.workshopData;

  protected readonly breadcrumbs = computed<readonly Breadcrumb[]>(() => {
    const path = this.currentUrl().split(/[?#]/, 1)[0];
    if (path.startsWith('/vulnerabilities/')) {
      return [
        { label: 'แดชบอร์ด', path: '/dashboard' },
        { label: 'ช่องโหว่', path: '/vulnerabilities' },
        { label: 'รายละเอียด' },
      ];
    }

    const page = this.navItems().find((item) => item.path === path);
    if (!page || page.path === '/dashboard') return [{ label: 'แดชบอร์ด' }];
    return [{ label: 'แดชบอร์ด', path: '/dashboard' }, { label: page.label }];
  });

  constructor() {
    this.auth.sessionExpired.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.closeMobileMenu();
      void this.router.navigate(['/login'], {
        queryParams: { reason: 'expired', returnUrl: safeReturnUrl(this.router.url) }, replaceUrl: true,
      });
    });
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => {
        this.currentUrl.set(event.urlAfterRedirects);
        this.searchValue.set(this.router.parseUrl(event.urlAfterRedirects).queryParams['q'] ?? '');
        this.closeMobileMenu();
      });
  }

  protected toggleSidebar(): void {
    this.sidebarCollapsed.update((collapsed) => !collapsed);
  }

  protected openMobileMenu(): void {
    const dialog = this.mobileDialog?.nativeElement;
    if (!dialog || dialog.open) return;

    this.menuOpen.set(true);
    dialog.showModal();
  }

  protected closeMobileMenu(): void {
    this.menuOpen.set(false);
    const dialog = this.mobileDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  protected onMobileDialogCancel(event: Event): void {
    event.preventDefault();
    this.closeMobileMenu();
  }

  protected onMobileDialogClick(event: MouseEvent): void {
    if (event.target === this.mobileDialog?.nativeElement) this.closeMobileMenu();
  }

  protected onMobileDialogClose(): void {
    this.menuOpen.set(false);
    this.mobileMenuButton?.nativeElement.focus({ preventScroll: true });
  }

  protected submitSearch(event: SubmitEvent, query: string): void {
    event.preventDefault();
    const value = query.trim();
    void this.router.navigate(['/vulnerabilities'], {
      queryParams: { q: value || null },
    });
  }

  protected logout(): void {
    this.closeMobileMenu();
    this.auth.logout().subscribe(() => void this.router.navigate(['/login'], { replaceUrl: true }));
  }

  @HostListener('document:click')
  @HostListener('document:keydown')
  protected recordActivity(): void { this.auth.recordActivity(); }

  @HostListener('window:focus')
  protected checkSession(): void { this.auth.checkSession(); }

  @HostListener('document:visibilitychange')
  protected checkVisibleSession(): void {
    if (document.visibilityState === 'visible') this.auth.checkSession();
  }
}
