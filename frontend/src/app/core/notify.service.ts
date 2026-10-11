import { Injectable } from '@angular/core';
import Swal, { SweetAlertIcon } from 'sweetalert2';
import { ApiError } from './models';

/** Every toast: top-right, dismissed automatically after five seconds, with a progress bar. */
const toast = Swal.mixin({
  toast: true,
  position: 'top-end',
  showConfirmButton: false,
  timer: 5000,
  timerProgressBar: true,
  didOpen: el => {
    el.addEventListener('mouseenter', Swal.stopTimer);
    el.addEventListener('mouseleave', Swal.resumeTimer);
  }
});

export interface ConfirmOptions {
  title: string;
  text?: string;
  confirmText?: string;
  danger?: boolean;
  icon?: SweetAlertIcon;
}

/** Thin wrapper over SweetAlert2 so every screen gives consistent feedback. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  success(message: string): void {
    toast.fire({ icon: 'success', title: message });
  }

  info(message: string): void {
    toast.fire({ icon: 'info', title: message });
  }

  warning(message: string): void {
    toast.fire({ icon: 'warning', title: message });
  }

  error(message: string, title = 'Something went wrong'): void {
    Swal.fire({ icon: 'error', title, text: message, confirmButtonText: 'OK' });
  }

  /** Shows the API's own message (already user-safe) or a generic fallback. */
  apiError(err: unknown, title = 'Could not complete the action'): void {
    this.error(messageOf(err), title);
  }

  async confirm(options: ConfirmOptions): Promise<boolean> {
    const result = await Swal.fire({
      icon: options.icon ?? (options.danger ? 'warning' : 'question'),
      title: options.title,
      text: options.text,
      showCancelButton: true,
      confirmButtonText: options.confirmText ?? 'Confirm',
      cancelButtonText: 'Cancel',
      reverseButtons: true,
      focusCancel: options.danger,
      customClass: { confirmButton: options.danger ? 'danger' : '' }
    });
    return result.isConfirmed;
  }

  /** Confirmation that asks for a short reason (used for rejecting requests). */
  async prompt(title: string, label: string, required = true, confirmText = 'Submit'): Promise<string | null> {
    const result = await Swal.fire({
      title,
      input: 'textarea',
      inputLabel: label,
      inputAttributes: { maxlength: '1000' },
      showCancelButton: true,
      confirmButtonText: confirmText,
      reverseButtons: true,
      inputValidator: value => (required && !value?.trim() ? 'Please enter a reason.' : null)
    });
    return result.isConfirmed ? (result.value as string ?? '').trim() : null;
  }
}

export function messageOf(err: unknown): string {
  const body = (err as { error?: ApiError })?.error;
  if (body && typeof body === 'object' && typeof body.message === 'string') return body.message;
  const status = (err as { status?: number })?.status;
  if (status === 0) return 'Cannot reach the server. Check your connection and try again.';
  return 'An unexpected error occurred. Please try again.';
}

/** Field-level errors from a 400 response, keyed by camelCase field name. */
export function fieldErrorsOf(err: unknown): Record<string, string> {
  const errors = (err as { error?: ApiError })?.error?.errors ?? {};
  return Object.fromEntries(Object.entries(errors).map(([k, v]) => [k, v[0]]));
}
