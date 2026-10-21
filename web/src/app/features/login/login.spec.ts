import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Login } from './login';

@Component({ standalone: true, template: '' })
class TestPage {}

describe('Login', () => {
  let harness: RouterTestingHarness;
  let auth: { mode: 'demo'; login: ReturnType<typeof vi.fn> };
  let router: Router;

  async function openLogin(url = '/login') {
    const component = await harness.navigateByUrl(url, Login);
    harness.detectChanges();
    return component;
  }

  function submitForm(): void {
    const form = harness.routeNativeElement!.querySelector('form')!;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.detectChanges();
  }

  function fillCredentials(username = 'admin', password = 'SvmDemo!2026'): void {
    const root = harness.routeNativeElement!;
    const usernameInput = root.querySelector<HTMLInputElement>('#login-username')!;
    const passwordInput = root.querySelector<HTMLInputElement>('#login-password')!;
    usernameInput.value = username;
    usernameInput.dispatchEvent(new Event('input', { bubbles: true }));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input', { bubbles: true }));
    harness.detectChanges();
  }

  beforeEach(async () => {
    auth = { mode: 'demo', login: vi.fn<(username: string, password: string) => Observable<void>>() };
    auth.login.mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([
          { path: 'login', component: Login },
          { path: 'dashboard', component: TestPage },
          { path: 'vulnerabilities', component: TestPage },
        ]),
        { provide: AuthService, useValue: auth },
      ],
    }).compileComponents();
    harness = await RouterTestingHarness.create();
    router = TestBed.inject(Router);
  });

  it('shows required validation on submit without calling auth', async () => {
    await openLogin();
    submitForm();

    expect(auth.login).not.toHaveBeenCalled();
    expect(harness.routeNativeElement?.querySelector('#username-error')?.textContent).toContain('ชื่อผู้ใช้');
    expect(harness.routeNativeElement?.querySelector('#password-error')?.textContent).toContain('รหัสผ่าน');
  });

  it('shows and focuses the supplied generic credential error', async () => {
    auth.login.mockReturnValue(throwError(() => new Error('Username or password is incorrect.')));
    await openLogin();
    fillCredentials('not-admin', 'wrong');
    submitForm();

    const alert = harness.routeNativeElement?.querySelector<HTMLElement>('[role="alert"]');
    expect(alert?.textContent).toContain('Username or password is incorrect.');
    expect(document.activeElement).toBe(alert);
  });

  it('navigates to the allowlisted return URL after successful sign-in', async () => {
    await openLogin('/login?returnUrl=%2Fvulnerabilities%3Frisk%3DHigh');
    fillCredentials();
    submitForm();
    await harness.fixture.whenStable();

    expect(auth.login).toHaveBeenCalledOnce();
    expect(auth.login).toHaveBeenCalledWith('admin', 'SvmDemo!2026');
    expect(router.url).toBe('/vulnerabilities?risk=High');
  });

  it('falls back to dashboard for an unsafe return destination', async () => {
    await openLogin('/login?returnUrl=https%3A%2F%2Fevil.example');
    fillCredentials();
    submitForm();
    await harness.fixture.whenStable();

    expect(router.url).toBe('/dashboard');
  });

  it('prevents duplicate requests while one login is pending', async () => {
    const pending = new Subject<void>();
    auth.login.mockReturnValue(pending);
    await openLogin();
    fillCredentials();
    submitForm();
    submitForm();

    expect(auth.login).toHaveBeenCalledOnce();
    expect(harness.routeNativeElement?.querySelector<HTMLButtonElement>('.submit-button')?.disabled).toBe(true);

    pending.next();
    pending.complete();
    await harness.fixture.whenStable();
  });

  it('toggles password visibility with an accessible action name', async () => {
    await openLogin();
    const root = harness.routeNativeElement!;
    const password = root.querySelector<HTMLInputElement>('#login-password')!;
    const toggle = root.querySelector<HTMLButtonElement>('.password-toggle')!;

    expect(password.type).toBe('password');
    expect(toggle.getAttribute('aria-label')).toBe('แสดงรหัสผ่าน');
    toggle.click();
    harness.detectChanges();
    expect(password.type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('ซ่อนรหัสผ่าน');
  });

  it('uses the required Thai labels and sign-in action', async () => {
    await openLogin();
    const root = harness.routeNativeElement!;

    expect(root.querySelector('label[for="login-username"]')?.textContent).toBe('ชื่อผู้ใช้');
    expect(root.querySelector('label[for="login-password"]')?.textContent).toBe('รหัสผ่าน');
    expect(root.querySelector<HTMLButtonElement>('.submit-button')?.textContent).toContain('เข้าสู่ระบบ');
  });

  it('announces an expired-session reason and keeps demo credentials visible', async () => {
    await openLogin('/login?reason=expired');

    const root = harness.routeNativeElement!;
    expect(root.querySelector('[role="status"]')?.textContent).toContain('เซสชันหมดอายุแล้ว');
    expect(root.textContent).toContain('admin');
    expect(root.textContent).toContain('SvmDemo!2026');
    expect(root.textContent).toContain('ไม่ได้ป้องกัน API');
  });
});
