import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from './models';

/** Query understood by every table endpoint (see GridQuery on the API). */
export interface GridQuery {
  page: number;
  pageSize: number;
  search?: string;
  sort?: string;
  dir?: 'asc' | 'desc';
  filters?: Record<string, string>;
}

export const API_BASE = '/api';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(path: string, params?: Record<string, string | number | boolean | undefined>): Observable<T> {
    return this.http.get<T>(`${API_BASE}/${path}`, { params: toParams(params) });
  }

  grid<T>(path: string, query: GridQuery, extra?: Record<string, string | number | boolean>): Observable<PagedResult<T>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.sort) params = params.set('sort', query.sort).set('dir', query.dir ?? 'asc');
    for (const [key, value] of Object.entries(query.filters ?? {})) {
      if (value !== undefined && value !== null && `${value}`.trim() !== '') params = params.set(`filters[${key}]`, value);
    }
    for (const [key, value] of Object.entries(extra ?? {})) params = params.set(key, value);
    return this.http.get<PagedResult<T>>(`${API_BASE}/${path}`, { params });
  }

  post<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.post<T>(`${API_BASE}/${path}`, body);
  }

  put<T>(path: string, body: unknown): Observable<T> {
    return this.http.put<T>(`${API_BASE}/${path}`, body);
  }

  delete<T = void>(path: string): Observable<T> {
    return this.http.delete<T>(`${API_BASE}/${path}`);
  }
}

function toParams(values?: Record<string, string | number | boolean | undefined>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values ?? {})) {
    if (value !== undefined && value !== '') params = params.set(key, value);
  }
  return params;
}
