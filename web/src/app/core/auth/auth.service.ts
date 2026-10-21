import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { Observable, ReplaySubject, Subject, catchError, finalize, map, of, tap } from 'rxjs';
import { AUTH_PROVIDER, AuthUser, DEMO_SESSION_KEY } from './auth.provider';
import { AuthTransport } from './auth.transport';
export type { AuthUser } from './auth.provider';
export { DEMO_SESSION_KEY } from './auth.provider';

interface DemoSession { mode: 'demo'; user: AuthUser; expiresAt: number; lastActivityAt: number; }
export const SESSION_IDLE_MS = 30 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly provider = inject(AUTH_PROVIDER);
  private readonly transport = inject(AuthTransport);
  private readonly currentUser = signal<AuthUser | null>(null);
  private readonly expiration = new Subject<void>();
  private readonly readySignal = new ReplaySubject<void>(1);
  private expiryTimer?: ReturnType<typeof setTimeout>;
  private demoDeadline: number | null = null;
  private lastActivityRequest = 0;
  readonly mode = this.provider.mode;
  readonly accessDenied = signal(false);
  readonly user = computed(() => this.currentUser());
  readonly sessionExpired = this.expiration.asObservable();
  readonly ready = this.readySignal.asObservable();
  readonly readyPromise: Promise<void>;

  constructor() {
    this.readyPromise = new Promise(resolve => {
      this.transport.invalidated.subscribe(() => this.expire());
      this.transport.accessDenied.subscribe(() => this.accessDenied.set(true));
      this.provider.restore().pipe(catchError(() => of(null))).subscribe(user => {
        if (this.provider.mode === 'demo') this.restoreDemoDeadline();
        else if (user) {
          this.currentUser.set(user);
          this.scheduleExpiry(this.transport.serverExpiry);
        }
        this.readySignal.next();
        resolve();
      });
    });
  }

  login(username: string, password: string): Observable<void> {
    return this.provider.login(username, password).pipe(tap(user => {
      this.currentUser.set(user);
      if (this.provider.mode === 'demo') this.saveDemoSession(user);
      else this.scheduleExpiry(this.transport.serverExpiry);
    }), map(() => undefined));
  }

  isAuthenticated(): boolean {
    const user = this.currentUser();
    if (user) {
      const deadline = this.provider.mode === 'demo' ? this.demoDeadline : this.transport.serverExpiry;
      if (deadline !== null && deadline <= Date.now()) this.expire();
    }
    return this.currentUser() !== null;
  }

  checkSession(): void { this.isAuthenticated(); }

  recordActivity(): void {
    if (!this.isAuthenticated()) return;
    const now = Date.now();
    if (this.provider.mode === 'demo') {
      const session = this.readDemoSession();
      if (session && now - session.lastActivityAt >= 1000) this.saveDemoSession(session.user);
      return;
    }
    if (now - this.lastActivityRequest < 30_000) return;
    this.lastActivityRequest = now;
    this.provider.recordActivity().pipe(
      tap(() => this.scheduleExpiry(this.transport.serverExpiry)),
      catchError(() => of(undefined)),
    ).subscribe();
  }

  logout(): Observable<void> {
    clearTimeout(this.expiryTimer);
    return this.provider.logout().pipe(
      catchError(() => of(undefined)),
      finalize(() => { this.currentUser.set(null); this.removeDemoSession(); }),
    );
  }

  ngOnDestroy(): void { clearTimeout(this.expiryTimer); this.expiration.complete(); this.readySignal.complete(); }

  private restoreDemoDeadline(): void {
    const session = this.readDemoSession();
    if (!session || session.expiresAt <= Date.now()) { this.currentUser.set(null); this.removeDemoSession(); return; }
    this.currentUser.set(session.user);
    this.scheduleExpiry(session.expiresAt);
  }
  private readDemoSession(): DemoSession | null {
    if (this.provider.mode !== 'demo') return null;
    try {
      const raw = sessionStorage.getItem(DEMO_SESSION_KEY);
      if (!raw) return null;
      const value: unknown = JSON.parse(raw);
      if (!value || typeof value !== 'object' || !('mode' in value) || value.mode !== 'demo' ||
        !('expiresAt' in value) || typeof value.expiresAt !== 'number' || !Number.isFinite(value.expiresAt) ||
        !('lastActivityAt' in value) || typeof value.lastActivityAt !== 'number' || !Number.isFinite(value.lastActivityAt) || !('user' in value)) return null;
      const user = this.provider.restoreIdentity(value);
      return user ? { mode: 'demo', user, expiresAt: value.expiresAt, lastActivityAt: value.lastActivityAt } : null;
    } catch { return null; }
  }
  private saveDemoSession(user: AuthUser): void {
    const now = Date.now();
    const session: DemoSession = { mode: 'demo', user, lastActivityAt: now, expiresAt: now + SESSION_IDLE_MS };
    this.currentUser.set(user);
    try { sessionStorage.setItem(DEMO_SESSION_KEY, JSON.stringify(session)); } catch { /* Memory session remains usable. */ }
    this.scheduleExpiry(session.expiresAt);
  }
  private scheduleExpiry(deadline: number | null): void {
    clearTimeout(this.expiryTimer);
    if (this.provider.mode === 'demo') this.demoDeadline = deadline;
    if (deadline === null || !Number.isFinite(deadline)) return;
    this.expiryTimer = setTimeout(() => this.expire(), Math.max(0, deadline - Date.now()));
  }
  private expire(): void {
    const wasAuthenticated = this.currentUser() !== null;
    clearTimeout(this.expiryTimer);
    this.currentUser.set(null);
    if (this.provider.mode === 'api') this.transport.clearCredentials();
    this.removeDemoSession();
    if (wasAuthenticated) this.expiration.next();
  }
  private removeDemoSession(): void {
    if (this.provider.mode !== 'demo') return;
    try { sessionStorage.removeItem(DEMO_SESSION_KEY); } catch { /* Storage may be unavailable. */ }
  }
}
