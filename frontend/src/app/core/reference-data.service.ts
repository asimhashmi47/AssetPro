import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, shareReplay, tap } from 'rxjs';
import { ApiService } from './api.service';
import { AppSettings, Lookup } from './models';

export type LookupKind = 'departments' | 'designations' | 'locations' | 'brands' | 'asset-types';

export const LOOKUP_LABELS: Record<LookupKind, string> = {
  departments: 'Department',
  designations: 'Designation',
  locations: 'Location',
  brands: 'Brand',
  'asset-types': 'Asset Type'
};

/** Cached master data + settings shared by forms, filters and the shell. */
@Injectable({ providedIn: 'root' })
export class ReferenceDataService {
  private readonly api = inject(ApiService);
  private readonly cache = new Map<LookupKind, Observable<Lookup[]>>();

  readonly settings = signal<AppSettings>({ companyName: 'AssetPro', currency: 'USD', defaultPageSize: 10, warrantyAlertDays: 30 });

  lookups(kind: LookupKind): Observable<Lookup[]> {
    let cached = this.cache.get(kind);
    if (!cached) {
      cached = this.api.get<Lookup[]>(`lookups/${kind}`).pipe(shareReplay({ bufferSize: 1, refCount: false }));
      this.cache.set(kind, cached);
    }
    return cached;
  }

  /** Smart dropdown "Add new": persists, invalidates the cache and returns the created item. */
  create(kind: LookupKind, name: string, tagPrefix?: string): Observable<Lookup> {
    return this.api.post<Lookup>(`lookups/${kind}`, { name, tagPrefix: tagPrefix || null }).pipe(tap(() => this.invalidate(kind)));
  }

  invalidate(kind: LookupKind): void {
    this.cache.delete(kind);
  }

  loadSettings(): void {
    this.api.get<AppSettings>('settings').subscribe({ next: s => this.settings.set(s), error: () => undefined });
  }

  names(kind: LookupKind): Observable<{ value: string; label: string }[]> {
    return this.lookups(kind).pipe(map(items => items.map(i => ({ value: i.name, label: i.name }))));
  }
}
