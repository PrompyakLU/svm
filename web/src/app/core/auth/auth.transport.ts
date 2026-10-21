import { HttpBackend, HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subject, catchError, concatMap, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AuthUser {
  id: string;
  username: string;
  displayName: string;
  roles: string[];
  permissions: string[];
  scopes: { type: string; value: string }[];
}
export interface AuthSessionResponse {
  token: string;
  expiresAt: string;
  idleExpiresAt: string;
  sessionExpiresAt: string;
  user: AuthUser;
}
export interface AuthMeResponse {
  user: AuthUser;
  idleExpiresAt: string;
  sessionExpiresAt: string;
}

/** Raw HTTP auth channel: auth calls bypass interceptors to avoid refresh recursion. */
@Injectable({ providedIn: 'root' })
export class AuthTransport {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly base = environment.apiBaseUrl.replace(/\/$/, '');
  private token: string | null = null;
  private csrfToken: string | null = null;
  private refreshRequest?: Observable<AuthSessionResponse>;
  private readonly invalidatedSubscribers = new Set<() => void>();
  private readonly deniedSubject = new Subject<void>();
  private identity: string | null = null;
  private idleExpiresAt: number | null = null;
  private sessionExpiresAt: number | null = null;

  get accessToken(): string | null { return this.token; }
  get serverExpiry(): number | null {
    if (this.idleExpiresAt === null || this.sessionExpiresAt === null) return null;
    return Math.min(this.idleExpiresAt, this.sessionExpiresAt);
  }
  get invalidated(): Observable<void> {
    return new Observable(subscriber => {
      const listener = () => subscriber.next();
      this.invalidatedSubscribers.add(listener);
      return () => this.invalidatedSubscribers.delete(listener);
    });
  }
  get accessDenied(): Observable<void> { return this.deniedSubject.asObservable(); }
  reportAccessDenied(): void { this.deniedSubject.next(); }
  clearCredentials(): void { this.clear(); }

  restore(): Observable<AuthUser | null> {
    return this.refresh().pipe(map(response => response.user), catchError(() => { this.clear(); return of(null); }));
  }

  login(username: string, password: string): Observable<AuthSessionResponse> {
    return this.getCsrf().pipe(
      concatMap(() => this.http.post<AuthSessionResponse>(`${this.base}/auth/login`, { username, password }, { withCredentials: true, headers: this.csrfHeaders() })),
      concatMap(response => this.acceptIdentity(response)),
    );
  }

  refresh(): Observable<AuthSessionResponse> {
    if (this.refreshRequest) return this.refreshRequest;
    this.refreshRequest = this.getCsrf().pipe(
      concatMap(() => this.http.post<AuthSessionResponse>(`${this.base}/auth/refresh`, {}, { withCredentials: true, headers: this.csrfHeaders() })),
      concatMap(response => this.acceptIdentity(response)),
      catchError(error => { this.invalidate(); return throwError(() => error); }),
      finalize(() => { this.refreshRequest = undefined; }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.refreshRequest;
  }

  activity(): Observable<void> {
    return this.getCsrf().pipe(
      concatMap(() => this.http.post<AuthMeResponse>(`${this.base}/auth/activity`, {}, { headers: this.authHeaders().set('X-XSRF-TOKEN', this.csrfToken!) })),
      tap(response => {
        this.idleExpiresAt = Date.parse(response.idleExpiresAt);
        this.sessionExpiresAt = Date.parse(response.sessionExpiresAt);
      }),
      map(() => undefined),
      catchError(error => { if (error instanceof HttpErrorResponse && error.status === 401) this.invalidate(); return throwError(() => error); }),
    );
  }

  logout(): Observable<void> {
    const request = this.token
      ? this.getCsrf().pipe(concatMap(() => this.http.post<void>(`${this.base}/auth/logout`, {}, { headers: this.authHeaders().set('X-XSRF-TOKEN', this.csrfToken!) })))
      : of(undefined);
    return request.pipe(map(() => undefined), catchError(() => of(undefined)), finalize(() => this.clear()));
  }

  invalidate(): void {
    const wasAuthenticated = this.token !== null;
    this.clear();
    if (wasAuthenticated) for (const notify of this.invalidatedSubscribers) notify();
  }

  private acceptIdentity(response: AuthSessionResponse): Observable<AuthSessionResponse> {
    if (!response || typeof response.token !== 'string' || !response.token || !response.user?.id) {
      this.clear();
      return throwError(() => new Error('Invalid authentication response'));
    }
    const changed = this.identity !== response.user.id;
    this.token = response.token;
    this.identity = response.user.id;
    this.idleExpiresAt = Date.parse(response.idleExpiresAt);
    this.sessionExpiresAt = Date.parse(response.sessionExpiresAt);
    // The request token is deliberately rotated after every successful identity/session exchange.
    if (changed || this.csrfToken) this.csrfToken = null;
    return this.getCsrf().pipe(map(() => response));
  }

  private getCsrf(): Observable<void> {
    if (this.csrfToken) return of(undefined);
    return this.http.get<{ token: string }>(`${this.base}/auth/csrf`, { withCredentials: true }).pipe(
      tap(response => { this.csrfToken = response.token; }),
      map(() => undefined),
    );
  }
  private authHeaders(): HttpHeaders { return new HttpHeaders(this.token ? { Authorization: `Bearer ${this.token}` } : {}); }
  private csrfHeaders(): HttpHeaders { return new HttpHeaders({ 'X-XSRF-TOKEN': this.csrfToken! }); }
  private clear(): void { this.token = null; this.csrfToken = null; this.identity = null; this.idleExpiresAt = null; this.sessionExpiresAt = null; }
}
