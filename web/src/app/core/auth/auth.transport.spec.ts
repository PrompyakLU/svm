import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { AUTH_PROVIDER, ApiAuthProvider } from './auth.provider';
import { authInterceptor } from './auth.interceptor';
import { AuthSessionResponse, AuthTransport } from './auth.transport';

const session = (token = 'access-1'): AuthSessionResponse => ({
  token, expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
  idleExpiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
  sessionExpiresAt: new Date(Date.now() + 8 * 60 * 60_000).toISOString(),
  user: { id: 'u1', username: 'operator', displayName: 'Operator', roles: ['Reader'], permissions: ['dashboard.read'], scopes: [] },
});

describe('API auth transport and interceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let transport: AuthTransport;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
      { provide: AUTH_PROVIDER, useClass: ApiAuthProvider },
    ] });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    transport = TestBed.inject(AuthTransport);
  });
  afterEach(() => http.verify());

  function login(): Promise<void> {
    const pending = firstValueFrom(transport.login('operator', 'secret')).then(() => undefined);
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-before' });
    const loginRequest = http.expectOne('/api/v1/auth/login');
    expect(loginRequest.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-before');
    loginRequest.flush(session());
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-after' });
    return pending;
  }

  it('keeps bearer credentials in memory and obtains a fresh CSRF token after login', async () => {
    await login();
    expect(transport.accessToken).toBe('access-1');
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
  });

  it('coalesces concurrent refreshes and sends the CSRF header', async () => {
    const first = firstValueFrom(transport.refresh());
    const second = firstValueFrom(transport.refresh());
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-refresh' });
    const refresh = http.expectOne('/api/v1/auth/refresh');
    expect(refresh.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-refresh');
    refresh.flush(session('rotated'));
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-rotated' });
    const [a, b] = await Promise.all([first, second]);
    expect(a.token).toBe('rotated');
    expect(b.token).toBe('rotated');
    expect(transport.accessToken).toBe('rotated');
  });

  it('never attaches bearer credentials to an external origin', async () => {
    await login();
    const pending = firstValueFrom(client.get('https://external.invalid/private'));
    const request = http.expectOne('https://external.invalid/private');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ ok: true });
    await pending;
  });

  it('refreshes and retries one protected 401, while a 403 leaves auth intact', async () => {
    await login();
    const retryable = firstValueFrom(client.get('/api/v1/instances'));
    http.expectOne('/api/v1/instances').flush({}, { status: 401, statusText: 'Unauthorized' });
    const refreshed = http.expectOne('/api/v1/auth/refresh');
    refreshed.flush(session('access-2'));
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-next' });
    const retry = http.expectOne('/api/v1/instances');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush({ items: [], totalCount: 0, page: 1, pageSize: 10 });
    await retryable;

    const denied = firstValueFrom(client.get('/api/v1/admin/configuration')).catch(error => error);
    http.expectOne('/api/v1/admin/configuration').flush({}, { status: 403, statusText: 'Forbidden' });
    await denied;
    expect(transport.accessToken).toBe('access-2');
  });

  it('waits for cookie restoration before readiness and clears credentials after remote logout', async () => {
    const auth = TestBed.inject(AuthService);
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-start' });
    http.expectOne('/api/v1/auth/refresh').flush(session('restored'));
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-restored' });
    await auth.readyPromise;
    expect(auth.user()?.username).toBe('operator');

    const logout = firstValueFrom(auth.logout());
    const request = http.expectOne('/api/v1/auth/logout');
    expect(request.request.headers.get('Authorization')).toBe('Bearer restored');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-restored');
    expect(transport.accessToken).toBe('restored');
    expect(auth.isAuthenticated()).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await logout;
    expect(transport.accessToken).toBeNull();
    expect(auth.user()).toBeNull();
  });

  it.each([401, 500])('clears client identity even when remote logout returns %s', async status => {
    const auth = TestBed.inject(AuthService);
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-start' });
    http.expectOne('/api/v1/auth/refresh').flush(session('restored'));
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-restored' });
    await auth.readyPromise;
    expect(auth.isAuthenticated()).toBe(true);

    const logout = firstValueFrom(auth.logout());
    http.expectOne('/api/v1/auth/logout').flush({}, { status, statusText: 'Logout rejected' });
    await logout;
    expect(transport.accessToken).toBeNull();
    expect(auth.user()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('expires API sessions before sending activity when the clock advances past the server deadline', async () => {
    const auth = TestBed.inject(AuthService);
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-start' });
    http.expectOne('/api/v1/auth/refresh').flush(session('restored'));
    http.expectOne('/api/v1/auth/csrf').flush({ token: 'csrf-restored' });
    await auth.readyPromise;
    const expired = vi.fn();
    auth.sessionExpired.subscribe(expired);
    const clock = vi.spyOn(Date, 'now').mockReturnValue(transport.serverExpiry! + 1);
    try {
      auth.recordActivity();
      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.user()).toBeNull();
      expect(transport.accessToken).toBeNull();
      expect(expired).toHaveBeenCalledOnce();
      http.expectNone('/api/v1/auth/activity');
    } finally {
      clock.mockRestore();
    }
  });
});
