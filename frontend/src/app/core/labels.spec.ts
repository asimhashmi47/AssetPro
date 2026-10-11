import { REQUEST_FLOW, actionVisual, initials, label, statusTone } from './labels';
import { fieldErrorsOf, messageOf } from './notify.service';

describe('labels', () => {
  it('humanises enum values', () => {
    expect(label('UnderRepair')).toBe('Under Repair');
    expect(label('NewAsset')).toBe('New Asset');
    expect(label(null)).toBe('—');
  });

  it('maps statuses to tones', () => {
    expect(statusTone('InStock')).toBe('success');
    expect(statusTone('Assigned')).toBe('primary');
    expect(statusTone('Rejected')).toBe('danger');
    expect(statusTone('Unknown')).toBe('neutral');
  });

  it('defines the request happy path in order', () => {
    expect(REQUEST_FLOW).toEqual(['Submitted', 'Approved', 'InProgress', 'Delivered', 'Checking', 'Received', 'Done']);
  });

  it('builds initials', () => {
    expect(initials('Muhammad Ali')).toBe('MA');
    expect(initials('Zara')).toBe('Z');
    expect(initials('')).toBe('?');
  });

  it('gives every history action an icon', () => {
    expect(actionVisual('ComponentReplaced').icon).toBe('cpu');
    expect(actionVisual('Scrapped').tone).toBe('danger');
  });
});

describe('API error helpers', () => {
  it('uses the server message when present', () => {
    expect(messageOf({ error: { status: 409, title: 'Conflict', message: 'Already assigned.' } })).toBe('Already assigned.');
  });

  it('explains network failures', () => {
    expect(messageOf({ status: 0 })).toContain('Cannot reach the server');
  });

  it('extracts the first error per field', () => {
    expect(fieldErrorsOf({ error: { errors: { email: ['Taken', 'Other'] } } })).toEqual({ email: 'Taken' });
  });
});
