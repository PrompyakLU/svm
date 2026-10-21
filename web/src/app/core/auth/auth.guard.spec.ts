import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { firstValueFrom, of } from 'rxjs';
import { authGuard, guestGuard, hasAnyPermission, safeReturnUrl } from './auth.guard';
import { AuthService, DEMO_SESSION_KEY } from './auth.service';
import { AUTH_PROVIDER, DemoAuthProvider } from './auth.provider';

@Component({ template: '<p>Route test</p>' })
class TestView {}

describe('Auth route boundaries', () => {
  beforeEach(() => {
    sessionStorage.removeItem(DEMO_SESSION_KEY);
    TestBed.configureTestingModule({ providers: [provideRouter([
      { path: 'login', component: TestView, canActivate: [guestGuard] },
      { path: '', canActivateChild: [authGuard], children: [
        { path: 'dashboard', component: TestView },
        { path: 'vulnerabilities/:id', component: TestView },
        { path: 'admin', component: TestView, data: { permissions: ['admin.masterdata', 'admin.users'] } },
      ] },
    ]), { provide: AUTH_PROVIDER, useClass: DemoAuthProvider }] });
  });

  it('redirects a signed-out deep link and preserves its safe destination', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/vulnerabilities/v1?source=queue');
    const router = TestBed.inject(Router);
    const url = router.parseUrl(router.url);
    expect(router.url).toContain('/login?');
    expect(url.queryParams['returnUrl']).toBe('/vulnerabilities/v1?source=queue');
  });

  it('allows protected navigation after login, skips login, and blocks access after logout', async () => {
    await firstValueFrom(TestBed.inject(AuthService).login('admin', 'SvmDemo!2026'));
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);
    await harness.navigateByUrl('/vulnerabilities/v1');
    expect(router.url).toBe('/vulnerabilities/v1');
    await harness.navigateByUrl('/login?returnUrl=https://evil.example');
    expect(router.url).toBe('/dashboard');
    await firstValueFrom(TestBed.inject(AuthService).logout());
    await harness.navigateByUrl('/vulnerabilities/v1');
    expect(router.url).toContain('/login?');
  });

  it('accepts only allowlisted internal destinations and rejects login loops and external redirects', () => {
    expect(safeReturnUrl('/vulnerabilities/v1?q=CVE-2026')).toBe('/vulnerabilities/v1?q=CVE-2026');
    for (const value of ['https://evil.example', '//evil.example', '/\\evil.example', '/login', '/login?returnUrl=/login', '/unknown', '/dashboard\n//evil.example', '/dashboard?x=%E0%A4%A', '/dashboard?x=%0A']) {
      expect(safeReturnUrl(value)).toBe('/dashboard');
    }
  });

  it.each([
    [['admin.users'], true],
    [['Admin.*'], true],
    [['admin.read'], false],
    [['dashboard.ops'], false],
    [['*'], false],
    [[], false],
  ] as const)('gates permission metadata for grants %j (allowed: %s)', async (permissions, allowed) => {
    const provider = TestBed.inject(AUTH_PROVIDER);
    const demoUser = await firstValueFrom(provider.login('admin', 'SvmDemo!2026'));
    vi.spyOn(provider, 'login').mockReturnValue(of({ ...demoUser, permissions: [...permissions] }));
    const auth = TestBed.inject(AuthService);
    await firstValueFrom(auth.login('admin', 'SvmDemo!2026'));
    const harness = await RouterTestingHarness.create('/dashboard');
    await harness.navigateByUrl('/admin');
    expect(TestBed.inject(Router).url).toBe(allowed ? '/admin' : '/dashboard');
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessDenied()).toBe(!allowed);
    if (!allowed) {
      await harness.navigateByUrl('/vulnerabilities/v1');
      expect(TestBed.inject(Router).url).toBe('/vulnerabilities/v1');
      expect(auth.accessDenied()).toBe(false);
    }
  });

  it('sends signed-out permission-gated links to login before checking permissions', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin');
    const router = TestBed.inject(Router);
    expect(router.url).toContain('/login?');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe('/admin');
  });
});

describe('Permission matching', () => {
  it.each([
    [['Instance.Read'], ['instance.read'], true],
    [['dashboard.exec'], ['dashboard.ops', 'dashboard.exec'], true],
    [['admin.*'], ['ADMIN.USERS'], true],
    [['instance.risk.*'], ['instance.risk.adjust'], true],
    [['admin.*'], ['instance.read'], false],
    [['admin.*'], ['administrator.users'], false],
    [['admin.*'], ['admin'], false],
    [['admin.*'], ['admin.'], false],
    [['admin.users'], ['admin.users.read'], false],
    [['*'], ['instance.read'], false],
    [['*'], ['*'], false],
    [['*.*', '.*', '**'], ['admin.users'], false],
    [[''], ['instance.read'], false],
    [['admin.*'], [''], false],
    [[], ['instance.read'], false],
    [[], [], true],
  ] as const)('matches %j against %j as %s', (granted, required, expected) => {
    expect(hasAnyPermission(granted, required)).toBe(expected);
  });
});
