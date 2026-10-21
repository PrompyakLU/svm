import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { map, take } from 'rxjs';
import { AuthService } from './auth.service';

/** Only known in-app destinations can be resumed. This also excludes /login redirect loops. */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || /[\\\r\n]/.test(value)) return '/dashboard';
  try {
    if (/[\u0000-\u001f\u007f\\]/.test(decodeURI(value))) return '/dashboard';
  } catch { return '/dashboard'; }
  return /^\/(dashboard|vulnerabilities(?:\/[A-Za-z0-9_-]+)?|intake|remediation|verification|admin|audit)(?:[?#][^\r\n]*)?$/.test(value)
    ? value : '/dashboard';
}

/** Match backend permission codes exactly or within a granted family (for example admin.*). */
export function hasAnyPermission(granted: readonly string[], required: readonly string[]): boolean {
  return required.length === 0 || required.some(permission => granted.some(grant => {
    if (!grant.trim() || !permission.trim() || grant === '*') return false;
    const normalizedGrant = grant.toLowerCase();
    const normalizedPermission = permission.toLowerCase();
    if (normalizedGrant === normalizedPermission) return true;
    const family = normalizedGrant.slice(0, -1);
    return normalizedGrant.length > 2 && normalizedGrant.endsWith('.*') &&
      normalizedPermission.startsWith(family) && normalizedPermission.length > family.length;
  }));
}

export const authGuard: CanActivateChildFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ready.pipe(take(1), map(() => {
    if (!auth.isAuthenticated()) return router.createUrlTree(['/login'], {
      queryParams: { returnUrl: safeReturnUrl(state.url) },
    });
    const required: string[] = route.data['permissions'] ?? [];
    const allowed = hasAnyPermission(auth.user()?.permissions ?? [], required);
    auth.accessDenied.set(!allowed);
    return allowed;
  }));
};

export const guestGuard: CanActivateFn = route => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ready.pipe(take(1), map(() => auth.isAuthenticated()
    ? router.parseUrl(safeReturnUrl(route.queryParamMap.get('returnUrl')))
    : true));
};
