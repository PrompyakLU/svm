import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable, defer, map, of, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthTransport, AuthSessionResponse, AuthUser } from './auth.transport';

export type { AuthUser } from './auth.transport';

/** Authentication boundary shared by the explicit demo and server backed modes. */
export abstract class AuthProvider {
  abstract readonly mode: 'demo' | 'api';
  abstract restore(): Observable<AuthUser | null>;
  abstract login(username: string, password: string): Observable<AuthUser>;
  abstract recordActivity(): Observable<void>;
  abstract logout(): Observable<void>;
  abstract restoreIdentity(value: unknown): AuthUser | null;
}

const DEMO_USER: AuthUser = {
  id: 'u7', username: 'admin', displayName: 'ผู้ดูแลระบบ', roles: ['Administrator'],
  // Explicit workshop grants use the same codes as the backend PermissionCatalog.
  permissions: [
    'asset.read', 'asset.write', 'asset.criticality', 'asset.sync',
    'finding.import', 'finding.create', 'finding.dedup', 'reference.read',
    'instance.read', 'instance.risk.adjust', 'instance.transition', 'instance.assign', 'instance.approve',
    'task.read', 'task.update', 'evidence.upload', 'comment.write',
    'verification.submit', 'verification.rescan', 'verification.record',
    'dashboard.ops', 'dashboard.exec',
    'admin.masterdata', 'admin.workflow', 'admin.sla', 'admin.risk', 'admin.assignment', 'admin.users',
    'audit.read', 'audit.export',
  ], scopes: [],
};

@Injectable({ providedIn: 'root' })
export class DemoAuthProvider extends AuthProvider {
  readonly mode = 'demo';
  restore(): Observable<AuthUser | null> { return of(this.restoreIdentity(this.readStored()) ); }
  login(username: string, password: string): Observable<AuthUser> {
    return defer(() => {
      if (username.trim() !== 'admin' || password !== 'SvmDemo!2026') {
        return throwError(() => new Error('ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง'));
      }
      return of(this.demoUser());
    });
  }
  recordActivity(): Observable<void> { return of(undefined); }
  logout(): Observable<void> {
    try { sessionStorage.removeItem(DEMO_SESSION_KEY); } catch { /* Storage is optional in demo mode. */ }
    return of(undefined);
  }
  restoreIdentity(value: unknown): AuthUser | null {
    const candidate = value && typeof value === 'object' && 'user' in value ? value.user : value;
    return candidate && typeof candidate === 'object' && 'id' in candidate && candidate.id === DEMO_USER.id &&
      'username' in candidate && candidate.username === DEMO_USER.username ? this.demoUser() : null;
  }
  private readStored(): unknown {
    try { const value = sessionStorage.getItem(DEMO_SESSION_KEY); return value ? JSON.parse(value) : null; }
    catch { return null; }
  }
  private demoUser(): AuthUser { return { ...DEMO_USER, roles: [...DEMO_USER.roles], permissions: [...DEMO_USER.permissions], scopes: [] }; }
}

@Injectable({ providedIn: 'root' })
export class ApiAuthProvider extends AuthProvider {
  readonly mode = 'api';
  private readonly transport = inject(AuthTransport);
  restore(): Observable<AuthUser | null> { return this.transport.restore(); }
  login(username: string, password: string): Observable<AuthUser> {
    return this.transport.login(username, password).pipe(mapSessionUser);
  }
  recordActivity(): Observable<void> { return this.transport.activity(); }
  logout(): Observable<void> { return this.transport.logout(); }
  restoreIdentity(value: unknown): AuthUser | null { return isAuthUser(value) ? value : null; }
}

/** Test-only fail-closed implementation retained for the legacy unconfigured-mode spec. */
@Injectable({ providedIn: 'root' })
export class UnconfiguredAuthProvider extends AuthProvider {
  readonly mode = 'api';
  restore(): Observable<AuthUser | null> { return of(null); }
  login(): Observable<AuthUser> { return throwError(() => new Error('ยังไม่ได้เชื่อมต่อระบบยืนยันตัวตน')); }
  recordActivity(): Observable<void> { return of(undefined); }
  logout(): Observable<void> { return of(undefined); }
  restoreIdentity(): null { return null; }
}

function mapSessionUser<T extends AuthSessionResponse>(source: Observable<T>): Observable<AuthUser> {
  return source.pipe(map(response => response.user));
}

function isAuthUser(value: unknown): value is AuthUser {
  if (!value || typeof value !== 'object') return false;
  const user = value as Partial<AuthUser>;
  return typeof user.id === 'string' && typeof user.username === 'string' &&
    typeof user.displayName === 'string' && Array.isArray(user.roles) &&
    Array.isArray(user.permissions) && Array.isArray(user.scopes);
}

export const DEMO_SESSION_KEY = 'svm.demo.session.v1';
export const AUTH_PROVIDER = new InjectionToken<AuthProvider>('SVM auth provider', {
  providedIn: 'root',
  factory: () => environment.authMode === 'demo' ? inject(DemoAuthProvider) : inject(ApiAuthProvider),
});
