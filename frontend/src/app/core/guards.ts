import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.token) return true;
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.token ? inject(Router).parseUrl(auth.homeUrl()) : true;
};

/** Route data: { permissions: ['Assets.View'] } - user needs at least one. */
export const permissionGuard: CanActivateFn = route => {
  const auth = inject(AuthService);
  const required = (route.data['permissions'] as string[] | undefined) ?? [];
  if (required.length === 0 || auth.canAny(...required)) return true;
  return inject(Router).createUrlTree(['/forbidden']);
};

export const homeRedirect: CanActivateFn = () => inject(Router).parseUrl(inject(AuthService).homeUrl());
