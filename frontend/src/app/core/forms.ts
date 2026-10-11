import { DestroyRef, WritableSignal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl } from '@angular/forms';

/**
 * Drops server-side field errors as soon as the user edits the form again.
 * Without this a message like "Serial number is already registered" would stay on screen
 * while the user is typing the corrected value.
 */
export function clearServerErrorsOnChange(
  form: AbstractControl,
  errors: WritableSignal<Record<string, string>>,
  destroyRef: DestroyRef
): void {
  form.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe(() => {
    if (Object.keys(errors()).length > 0) errors.set({});
  });
}
