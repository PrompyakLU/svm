// SVM full end-to-end JOURNEY specs — one continuous video per flow (no ffmpeg).
// [journey][positive] = whole happy path; [journey][negative] = whole error path.
import { test, expect } from './_helpers.js';

const BASE = 'http://localhost:4200';
const ADMIN = { u: 'admin', p: 'SvmDemo!2026' };

test('TC_J01 full positive journey login to dashboard to key views [journey][positive]', async ({ page }) => {
  // login
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('#login-username')).toBeVisible();
  await page.fill('#login-username', ADMIN.u);
  await page.fill('#login-password', ADMIN.p);
  await page.click('.submit-button');
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 30_000 });
  await expect(page.locator('.app-shell')).toBeVisible({ timeout: 30_000 });

  // walk the views admin is permitted to see (dashboard.ops + admin.* + finding.*)
  for (const path of ['/dashboard', '/intake', '/admin']) {
    await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded' });
    await expect(page).toHaveURL(new RegExp(path.replace('/', '\\/')));
    await expect(page.locator('.app-shell')).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.access-denied')).toHaveCount(0);
    await page.waitForTimeout(400);
  }

  // logout to close the loop
  await page.goto(`${BASE}/dashboard`, { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: 'ออกจากระบบ' }).first().click();
  await page.waitForURL(/\/login/, { timeout: 20_000 });
  await expect(page.locator('#login-username')).toBeVisible();
});

test('TC_J02 full negative journey bad login and guarded access [journey][negative]', async ({ page }) => {
  // 1) protected URL while unauthenticated -> redirect to login
  await page.goto(`${BASE}/admin`, { waitUntil: 'domcontentloaded' });
  await page.waitForURL(/\/login/, { timeout: 20_000 });
  await expect(page.locator('#login-username')).toBeVisible();

  // 2) submit empty -> field validation
  await page.click('.submit-button');
  await expect(page.locator('#username-error')).toBeVisible();
  await expect(page.locator('#password-error')).toBeVisible();

  // 3) wrong password -> generic error, stays on login
  await page.fill('#login-username', 'admin');
  await page.fill('#login-password', 'wrong-password-value');
  await page.click('.submit-button');
  await expect(page.locator('[role="alert"].login-error')).toBeVisible({ timeout: 20_000 });
  await expect(page).toHaveURL(/\/login/);
});
