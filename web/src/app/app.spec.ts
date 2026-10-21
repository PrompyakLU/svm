import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { App } from './app';
import { firstValueFrom } from 'rxjs';
import { AuthService, DEMO_SESSION_KEY } from './core/auth/auth.service';
import { AUTH_PROVIDER, DemoAuthProvider } from './core/auth/auth.provider';

@Component({
  standalone: true,
  template: '<p>Test route content</p>',
})
class TestRoute {}

const testRoutes: Routes = [
  { path: 'login', component: TestRoute },
  { path: 'dashboard', component: TestRoute },
  { path: 'vulnerabilities', component: TestRoute },
  { path: 'vulnerabilities/:id', component: TestRoute },
  { path: 'intake', component: TestRoute },
  { path: 'remediation', component: TestRoute },
  { path: 'verification', component: TestRoute },
  { path: 'admin', component: TestRoute },
  { path: 'audit', component: TestRoute },
];

describe('App shell', () => {
  beforeEach(async () => {
    sessionStorage.removeItem(DEMO_SESSION_KEY);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(testRoutes), { provide: AUTH_PROVIDER, useClass: DemoAuthProvider }],
    }).compileComponents();
    await firstValueFrom(TestBed.inject(AuthService).login('admin', 'SvmDemo!2026'));
  });

  it('renders all seven routes and marks only the current route active', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/vulnerabilities/42');
    fixture.detectChanges();
    await fixture.whenStable();

    const root: HTMLElement = fixture.nativeElement;
    const links = Array.from(
      root.querySelectorAll('.sidebar__nav .nav-link'),
    );
    expect(links).toHaveLength(7);
    expect(links.filter((link) => link.getAttribute('aria-current') === 'page')).toHaveLength(1);
    expect(links.find((link) => link.getAttribute('aria-current') === 'page')?.textContent).toContain('ช่องโหว่');
    expect(root.querySelector('.breadcrumbs [aria-current="page"]')?.textContent).toContain('รายละเอียด');
  });

  it('collapses the desktop sidebar with an accessible state', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const root: HTMLElement = fixture.nativeElement;
    const button = root.querySelector<HTMLButtonElement>('.sidebar__collapse')!;
    button.click();
    fixture.detectChanges();

    expect(root.querySelector('.app-shell')?.classList.contains('app-shell--collapsed')).toBe(true);
    expect(button.getAttribute('aria-expanded')).toBe('false');
  });

  it('shows the current identity and logs out into the public login view', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/dashboard');
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.account-identity')?.textContent).toContain('ผู้ดูแลระบบ');
    root.querySelector<HTMLButtonElement>('.sidebar .account-logout')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(router.url).toBe('/login');
    expect(TestBed.inject(AuthService).user()).toBeNull();
    expect(root.querySelector('.app-shell')).toBeNull();
  });

  it('routes global search to the vulnerability list with a q query parameter', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const input = root.querySelector<HTMLInputElement>('#global-search')!;
    const form = root.querySelector<HTMLFormElement>('.search-form')!;
    input.value = '  CVE-2026-1234  ';
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();

    expect(router.url).toBe('/vulnerabilities?q=CVE-2026-1234');
  });

  it('opens the mobile drawer, closes on Escape, and restores focus to its trigger', async () => {
    const prototype = HTMLDialogElement.prototype;
    const originalShowModal = Object.getOwnPropertyDescriptor(prototype, 'showModal');
    const originalClose = Object.getOwnPropertyDescriptor(prototype, 'close');
    Object.defineProperty(prototype, 'showModal', {
      configurable: true,
      value(this: HTMLDialogElement) {
        this.setAttribute('open', '');
      },
    });
    Object.defineProperty(prototype, 'close', {
      configurable: true,
      value(this: HTMLDialogElement) {
        this.removeAttribute('open');
        this.dispatchEvent(new Event('close'));
      },
    });

    try {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      const root: HTMLElement = fixture.nativeElement;
      const trigger = root.querySelector<HTMLButtonElement>('.topbar__menu-button')!;
      const dialog = root.querySelector<HTMLDialogElement>('.mobile-drawer')!;
      trigger.style.display = 'inline-flex';
      trigger.click();
      fixture.detectChanges();

      expect(dialog.hasAttribute('open')).toBe(true);
      expect(trigger.getAttribute('aria-expanded')).toBe('true');
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      await fixture.whenStable();
      fixture.detectChanges();

      expect(dialog.hasAttribute('open')).toBe(false);
      expect(trigger.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(trigger);
    } finally {
      if (originalShowModal) Object.defineProperty(prototype, 'showModal', originalShowModal);
      else Reflect.deleteProperty(prototype, 'showModal');
      if (originalClose) Object.defineProperty(prototype, 'close', originalClose);
      else Reflect.deleteProperty(prototype, 'close');
    }
  });
});
