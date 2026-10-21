import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthTransport } from './auth.transport';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const transport = inject(AuthTransport);
  const apiRoot = new URL(environment.apiBaseUrl, document.baseURI);
  let target: URL;
  try { target = new URL(request.url, document.baseURI); } catch { return next(request); }
  const apiPath = apiRoot.pathname.replace(/\/$/, '');
  if (target.origin !== apiRoot.origin || !(target.pathname === apiPath || target.pathname.startsWith(`${apiPath}/`))) {
    return next(request);
  }
  if (/\/auth\/(?:csrf|login|refresh|activity|logout)\/?$/.test(target.pathname)) return next(request);
  const token = transport.accessToken;
  const authorized = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  return next(authorized).pipe(catchError(error => {
    if (error instanceof HttpErrorResponse && error.status === 403) transport.reportAccessDenied();
    if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !token) return throwError(() => error);
    return transport.refresh().pipe(
      switchMap(() => {
        const refreshed = transport.accessToken;
        if (!refreshed) return throwError(() => error);
        return next(request.clone({ setHeaders: { Authorization: `Bearer ${refreshed}` } })).pipe(catchError(retryError => {
          if (retryError instanceof HttpErrorResponse && retryError.status === 401) transport.invalidate();
          return throwError(() => retryError);
        }));
      }),
    );
  }));
};
