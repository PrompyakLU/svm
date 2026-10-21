// SVM UI specs — Scale Suite (desktop 1440x900, api mode, real API on :5181)
// Users chosen to match each route's RBAC (hasAnyPermission on route.data.permissions):
//  - admin   (SYSTEM_ADMIN):     dashboard.ops, admin.*, finding.*, asset.*  -> /dashboard, /intake, /admin
//  - officer1 (VULN_MGMT_OFFICER): instance.read, dashboard.ops, finding.*   -> /vulnerabilities, /intake, /remediation
//  - auditor1 (AUDITOR):          audit.read, instance.read, dashboard.ops    -> /audit
import { test, expect } from './_helpers.js';

const BASE = 'http://localhost:4200';
const USERS = {
  admin: { u: 'admin', p: 'SvmDemo!2026' },
  officer1: { u: 'officer1', p: 'SvmDemo!2026' },
  auditor1: { u: 'auditor1', p: 'SvmDemo!2026' },
};

async function gotoLogin(page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('#login-username')).toBeVisible();
}

async function login(page, user) {
  await gotoLogin(page);
  await page.fill('#login-username', user.u);
  await page.fill('#login-password', user.p);
  await page.click('.submit-button');
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 30_000 });
  await expect(page.locator('.app-shell')).toBeVisible({ timeout: 30_000 });
}

// verify an in-app route rendered for a permitted user: shell present, no access-denied banner
async function expectRouteAllowed(page, pathRegex) {
  await expect(page).toHaveURL(pathRegex);
  await expect(page.locator('.app-shell')).toBeVisible();
  await expect(page.locator('.access-denied')).toHaveCount(0);
}

// --- TC_001 login success -> dashboard [positive] -------------------------

test('TC_001 login success redirects to dashboard [positive]', async ({ page }) => {
  await login(page, USERS.admin);
  await expect(page).toHaveURL(/\/dashboard/);
  // identity shown in the shell (role label), logout control present (sidebar + mobile drawer render it, take the first)
  await expect(page.getByText('SYSTEM_ADMIN').first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'ออกจากระบบ' }).first()).toBeVisible();
});

// --- TC_002 wrong credentials -> generic error [negative] -----------------

test('TC_002 invalid credentials show generic error [negative]', async ({ page }) => {
  await gotoLogin(page);
  await page.fill('#login-username', 'admin');
  await page.fill('#login-password', 'definitely-wrong-password');
  await page.click('.submit-button');
  await expect(page.locator('[role="alert"].login-error')).toBeVisible({ timeout: 20_000 });
  await expect(page).toHaveURL(/\/login/);
});

// --- TC_003 required fields block submission [negative] -------------------

test('TC_003 empty required fields block submission [negative]', async ({ page }) => {
  await gotoLogin(page);
  await page.click('.submit-button');
  await expect(page.locator('#username-error')).toBeVisible();
  await expect(page.locator('#password-error')).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
});

// --- TC_004 protected URL redirect then return [negative] -----------------

test('TC_004 protected URL redirects to login and returns after auth [negative]', async ({ page }) => {
  await page.goto(`${BASE}/vulnerabilities`, { waitUntil: 'domcontentloaded' });
  await page.waitForURL(/\/login/, { timeout: 20_000 });
  await expect(page.locator('#login-username')).toBeVisible();
  // officer1 has instance.read -> allowed to return to /vulnerabilities
  await page.fill('#login-username', USERS.officer1.u);
  await page.fill('#login-password', USERS.officer1.p);
  await page.click('.submit-button');
  await page.waitForURL(/\/vulnerabilities/, { timeout: 30_000 });
  await expect(page).toHaveURL(/\/vulnerabilities/);
});

// --- TC_010 vulnerability list (officer1 has instance.read) [positive] ----

test('TC_010 vulnerability list loads for permitted user [positive]', async ({ page }) => {
  await login(page, USERS.officer1);
  await page.goto(`${BASE}/vulnerabilities`, { waitUntil: 'domcontentloaded' });
  await expectRouteAllowed(page, /\/vulnerabilities/);
});

// --- TC_011 intake (officer1 has finding.import/create) [positive] --------

test('TC_011 intake page loads for permitted user [positive]', async ({ page }) => {
  await login(page, USERS.officer1);
  await page.goto(`${BASE}/intake`, { waitUntil: 'domcontentloaded' });
  await expectRouteAllowed(page, /\/intake/);
});

// --- TC_024 dashboard KPIs (admin has dashboard.ops) [positive] -----------

test('TC_024 dashboard shows operations view [positive]', async ({ page }) => {
  await login(page, USERS.admin);
  await page.goto(`${BASE}/dashboard`, { waitUntil: 'domcontentloaded' });
  await expectRouteAllowed(page, /\/dashboard/);
  // dashboard heading rendered (use heading role to avoid matching the loading text)
  await expect(page.getByRole('heading', { name: 'ภาพรวมความปลอดภัย' })).toBeVisible({ timeout: 20_000 });
});

// --- TC_025 admin config page (admin has admin.*) [positive] --------------

test('TC_025 admin configuration page loads [positive]', async ({ page }) => {
  await login(page, USERS.admin);
  await page.goto(`${BASE}/admin`, { waitUntil: 'domcontentloaded' });
  await expectRouteAllowed(page, /\/admin/);
});

// --- TC_028 logout then back does not reveal protected UI [negative] ------

test('TC_028 logout then browser back returns to login [negative]', async ({ page }) => {
  await login(page, USERS.admin);
  await page.goto(`${BASE}/dashboard`, { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: 'ออกจากระบบ' }).first().click();
  await page.waitForURL(/\/login/, { timeout: 20_000 });
  await page.goBack();
  await page.waitForTimeout(1500);
  // must not show an authenticated shell with a live session after logout
  await expect(page.locator('#login-username')).toBeVisible({ timeout: 20_000 });
});
