import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { permissionGuard } from './guards';
import { LoginResponse, SessionUser } from './models';

function login(user: Partial<SessionUser>, rememberMe = false): AuthService {
  const auth = TestBed.inject(AuthService);
  const http = TestBed.inject(HttpTestingController);
  auth.login('x@y.z', 'secret', rememberMe).subscribe();
  const response: LoginResponse = {
    token: 'token-123',
    expiresAt: new Date(Date.now() + 3600_000).toISOString(),
    user: { id: 1, email: 'x@y.z', fullName: 'Test User', role: 'User', employeeId: 2, permissions: [], ...user }
  };
  http.expectOne('/api/auth/login').flush(response);
  return auth;
}

describe('AuthService', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  });

  it('grants admins every permission', () => {
    const auth = login({ role: 'Admin' });
    expect(auth.can('Users.Manage')).toBe(true);
    expect(auth.homeUrl()).toBe('/dashboard');
  });

  it('limits standard users to granted permissions', () => {
    const auth = login({ permissions: ['Requests.Create'] });
    expect(auth.can('Requests.Create')).toBe(true);
    expect(auth.can('Assets.View')).toBe(false);
    expect(auth.homeUrl()).toBe('/my-workspace');
  });

  it('stores the session in sessionStorage unless "remember me" is set', () => {
    login({});
    expect(sessionStorage.getItem('assetpro.session')).not.toBeNull();
    expect(localStorage.getItem('assetpro.session')).toBeNull();
  });

  it('keeps the session in localStorage when "remember me" is set', () => {
    login({}, true);
    expect(localStorage.getItem('assetpro.session')).not.toBeNull();
  });

  it('drops an expired token', () => {
    const auth = login({});
    const raw = JSON.parse(sessionStorage.getItem('assetpro.session')!);
    raw.expiresAt = new Date(Date.now() - 1000).toISOString();
    sessionStorage.setItem('assetpro.session', JSON.stringify(raw));
    // A fresh service instance restores from storage and must reject the expired session.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    expect(TestBed.inject(AuthService).token).toBeNull();
    expect(auth).toBeTruthy();
  });

  it('permission guard redirects users without the permission', () => {
    login({ permissions: [] });
    const route = { data: { permissions: ['Assets.View'] } } as unknown as ActivatedRouteSnapshot;
    const result = TestBed.runInInjectionContext(() => permissionGuard(route, {} as RouterStateSnapshot));
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/forbidden');
  });

  it('permission guard lets users with the permission through', () => {
    login({ permissions: ['Assets.View'] });
    const route = { data: { permissions: ['Assets.View'] } } as unknown as ActivatedRouteSnapshot;
    expect(TestBed.runInInjectionContext(() => permissionGuard(route, {} as RouterStateSnapshot))).toBe(true);
  });
});
