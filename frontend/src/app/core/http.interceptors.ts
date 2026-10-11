import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, retry, throwError, timer } from 'rxjs';
import { API_BASE } from './api.service';
import { AuthService } from './auth.service';

/** Attaches the bearer token to API calls only (never to third-party URLs). */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = req.url.startsWith(API_BASE) ? auth.token : null;
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !req.url.endsWith('/auth/login')) auth.expire();
      return throwError(() => error);
    })
  );
};

/** Retries idempotent GETs once on network failure / 502-504, so a brief blip does not break a screen. */
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') return next(req);
  return next(req).pipe(
    retry({
      count: 1,
      delay: (error: HttpErrorResponse) =>
        error.status === 0 || error.status === 502 || error.status === 503 || error.status === 504
          ? timer(600)
          : throwError(() => error)
    })
  );
};
