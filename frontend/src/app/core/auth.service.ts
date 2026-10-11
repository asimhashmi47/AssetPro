import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, of, tap } from 'rxjs';
import { API_BASE } from './api.service';
import { LoginResponse, SessionUser } from './models';

const STORAGE_KEY = 'assetpro.session';

interface StoredSession {
  token: string;
  expiresAt: string;
  user: SessionUser;
}

/**
 * Holds the signed-in session. "Remember me" keeps it in localStorage, otherwise sessionStorage
 * (cleared when the browser closes). UI permission checks are convenience only - the API enforces everything.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<StoredSession | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.user()?.role === 'Admin');
  private readonly permissionSet = computed(() => new Set(this.user()?.permissions ?? []));

  get token(): string | null {
    const s = this.session();
    if (!s) return null;
    if (new Date(s.expiresAt).getTime() <= Date.now()) {
      this.clear();
      return null;
    }
    return s.token;
  }

  can(permission: string): boolean {
    return this.isAdmin() || this.permissionSet().has(permission);
  }

  canAny(...permissions: string[]): boolean {
    return permissions.some(p => this.can(p));
  }

  /** Where a user lands after sign-in: dashboard if allowed, otherwise their own workspace. */
  homeUrl(): string {
    return this.can('Dashboard.View') ? '/dashboard' : '/my-workspace';
  }

  login(email: string, password: string, rememberMe: boolean): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${API_BASE}/auth/login`, { email, password, rememberMe }).pipe(
      tap(response => {
        const stored: StoredSession = { token: response.token, expiresAt: response.expiresAt, user: response.user };
        this.clearStorage();
        try {
          (rememberMe ? localStorage : sessionStorage).setItem(STORAGE_KEY, JSON.stringify(stored));
        } catch {
          // Storage can be unavailable (private mode); the in-memory session still works for this tab.
        }
        this.session.set(stored);
      })
    );
  }

  logout(): void {
    const finish = () => {
      this.clear();
      this.router.navigateByUrl('/login');
    };
    if (!this.token) return finish();
    this.http.post(`${API_BASE}/auth/logout`, {}).pipe(catchError(() => of(null))).subscribe(finish);
  }

  /** Called by the interceptor on 401: drop the session and send the user to sign in. */
  expire(): void {
    if (!this.session()) return;
    this.clear();
    this.router.navigate(['/login'], { queryParams: { expired: 1, returnUrl: this.router.url } });
  }

  /** Refresh the cached user (e.g. after an admin changed this user's own profile). */
  refreshMe(): void {
    this.http.get<SessionUser>(`${API_BASE}/auth/me`).subscribe({
      next: user => {
        const s = this.session();
        if (!s) return;
        const updated = { ...s, user };
        this.session.set(updated);
        this.persist(updated);
      },
      error: () => undefined
    });
  }

  private clear(): void {
    this.session.set(null);
    this.clearStorage();
  }

  private persist(s: StoredSession): void {
    try {
      if (localStorage.getItem(STORAGE_KEY)) localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
      else sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    } catch { /* ignore */ }
  }

  private clearStorage(): void {
    try {
      localStorage.removeItem(STORAGE_KEY);
      sessionStorage.removeItem(STORAGE_KEY);
    } catch { /* ignore */ }
  }

  private restore(): StoredSession | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY) ?? sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as StoredSession;
      if (!parsed?.token || new Date(parsed.expiresAt).getTime() <= Date.now()) return null;
      return parsed;
    } catch {
      return null;
    }
  }
}
