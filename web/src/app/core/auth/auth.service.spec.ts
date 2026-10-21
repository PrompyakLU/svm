import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AuthService, DEMO_SESSION_KEY, SESSION_IDLE_MS } from './auth.service';
import { AUTH_PROVIDER, DemoAuthProvider, UnconfiguredAuthProvider } from './auth.provider';

async function signIn(service: AuthService): Promise<void> {
  const pending = firstValueFrom(service.login('admin', 'SvmDemo!2026'));
  await vi.advanceTimersByTimeAsync(250);
  await pending;
}

describe('Workshop authentication session', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    sessionStorage.removeItem(DEMO_SESSION_KEY);
    TestBed.configureTestingModule({ providers: [{ provide: AUTH_PROVIDER, useClass: DemoAuthProvider }] });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    sessionStorage.removeItem(DEMO_SESSION_KEY);
    vi.useRealTimers();
  });

  it('rejects invalid credentials without creating a session', async () => {
    const service = TestBed.inject(AuthService);
    await expect(firstValueFrom(service.login('admin', 'wrong'))).rejects.toThrow('ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง');
    expect(service.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(DEMO_SESSION_KEY)).toBeNull();
  });

  it('restores a per-tab session without storing the password and clears it on logout', async () => {
    const service = TestBed.inject(AuthService);
    await signIn(service);
    expect(service.user()?.roles).toEqual(['Administrator']);
    expect(sessionStorage.getItem(DEMO_SESSION_KEY)).not.toContain('SvmDemo!2026');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: AUTH_PROVIDER, useClass: DemoAuthProvider }] });
    const restored = TestBed.inject(AuthService);
    expect(restored.isAuthenticated()).toBe(true);
    expect(restored.user()?.username).toBe('admin');
    await firstValueFrom(restored.logout());
    expect(restored.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(DEMO_SESSION_KEY)).toBeNull();
  });

  it('renews inactivity on meaningful activity rather than expiring 30 minutes after login', async () => {
    const service = TestBed.inject(AuthService);
    await signIn(service);
    await vi.advanceTimersByTimeAsync(20 * 60 * 1000);
    service.recordActivity();
    await vi.advanceTimersByTimeAsync(20 * 60 * 1000);
    expect(service.isAuthenticated()).toBe(true);
    const expired = vi.fn();
    service.sessionExpired.subscribe(expired);
    await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    expect(service.isAuthenticated()).toBe(false);
    expect(expired).toHaveBeenCalledOnce();
  });

  it('restores canonical demo grants rather than trusting obsolete or injected saved permissions', async () => {
    const service = TestBed.inject(AuthService);
    await signIn(service);
    const stored = JSON.parse(sessionStorage.getItem(DEMO_SESSION_KEY)!);
    stored.user.permissions = ['*', 'dashboard.read', 'vulnerability.read'];
    sessionStorage.setItem(DEMO_SESSION_KEY, JSON.stringify(stored));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: AUTH_PROVIDER, useClass: DemoAuthProvider }] });
    const restored = TestBed.inject(AuthService);
    expect(restored.isAuthenticated()).toBe(true);
    expect(restored.user()?.permissions).toEqual(expect.arrayContaining([
      'dashboard.ops', 'dashboard.exec', 'instance.read', 'finding.import', 'finding.create', 'finding.dedup',
      'task.read', 'task.update', 'verification.submit', 'verification.rescan', 'verification.record',
      'admin.masterdata', 'admin.users', 'audit.read',
    ]));
    expect(restored.user()?.permissions).not.toContain('*');
    expect(restored.user()?.permissions).not.toContain('dashboard.read');
    expect(restored.user()?.permissions).not.toContain('vulnerability.read');
  });

  it.each(['activity', 'focus', 'guard'] as const)('cannot revive an expired session on %s after the clock advances while timers are suspended', async trigger => {
    const service = TestBed.inject(AuthService);
    await signIn(service);
    const expired = vi.fn();
    service.sessionExpired.subscribe(expired);
    vi.setSystemTime(Date.now() + SESSION_IDLE_MS + 1);
    if (trigger === 'activity') service.recordActivity();
    else if (trigger === 'focus') service.checkSession();
    expect(service.isAuthenticated()).toBe(false);
    service.recordActivity();
    service.checkSession();
    expect(service.user()).toBeNull();
    expect(expired).toHaveBeenCalledOnce();
    expect(sessionStorage.getItem(DEMO_SESSION_KEY)).toBeNull();
  });

  it.each([
    ['invalid JSON', '{'],
    ['unrelated identity', JSON.stringify({ mode: 'demo', expiresAt: Date.now() + SESSION_IDLE_MS, lastActivityAt: Date.now(), user: { id: 'someone' } })],
    ['missing activity timestamp', JSON.stringify({ mode: 'demo', expiresAt: Date.now() + SESSION_IDLE_MS, user: { id: 'u7', username: 'admin' } })],
    ['expired deadline', JSON.stringify({ mode: 'demo', expiresAt: Date.now() - 1, lastActivityAt: Date.now() - SESSION_IDLE_MS, user: { id: 'u7', username: 'admin' } })],
    ['invalid deadline', '{"mode":"demo","expiresAt":1e400,"lastActivityAt":0,"user":{"id":"u7","username":"admin"}}'],
    ['invalid activity timestamp', '{"mode":"demo","expiresAt":9999999999999,"lastActivityAt":1e400,"user":{"id":"u7","username":"admin"}}'],
  ])('removes a persisted demo session with %s', (_reason, stored) => {
    sessionStorage.setItem(DEMO_SESSION_KEY, stored);
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(DEMO_SESSION_KEY)).toBeNull();
  });

  it('fails closed in unconfigured real-auth mode instead of accepting the demo password', async () => {
    TestBed.overrideProvider(AUTH_PROVIDER, { useValue: new UnconfiguredAuthProvider() });
    const service = TestBed.inject(AuthService);
    await expect(firstValueFrom(service.login('admin', 'SvmDemo!2026'))).rejects.toThrow('ยังไม่ได้เชื่อมต่อระบบยืนยันตัวตน');
    expect(service.isAuthenticated()).toBe(false);
  });
});
