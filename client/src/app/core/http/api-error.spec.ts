import { describe, it, expect } from 'vitest';

import { apiErrorMessage } from './api-error';

/**
 * Every body in this file is one the API can actually produce. B1's original toMsg test
 * flushed { message: '…' } — a shape ApiBaseController.MapErrors cannot emit — so it passed
 * while the component rendered "[object Object]". Do not add a case for an invented body.
 */
describe('apiErrorMessage', () => {
  it('joins the descriptions of a Result<T> failure', () => {
    // ApiBaseController.MapErrors serialises IReadOnlyList<Error>, and Error is
    // record Error(string Code, string Description).
    const err = {
      status: 400,
      error: [
        { code: 'Validation', description: 'Name is required.' },
        { code: 'Validation', description: 'Key must be uppercase.' },
      ],
    };

    expect(apiErrorMessage(err, 'fallback')).toBe('Name is required.\nKey must be uppercase.');
  });

  it('never renders [object Object] for a Result<T> failure', () => {
    const err = { status: 409, error: [{ code: 'Conflict', description: 'Already linked.' }] };

    expect(apiErrorMessage(err, 'fallback')).not.toContain('[object Object]');
  });

  it('falls back when a Result<T> array carries no usable description', () => {
    const err = { status: 400, error: [{ code: 'Weird' }, { code: 'Odd', description: '  ' }] };

    expect(apiErrorMessage(err, 'fallback')).toBe('fallback');
  });

  it('flattens ValidationProblemDetails field errors', () => {
    // [ApiController] + AddFluentValidationAutoValidation with no
    // SuppressModelStateInvalidFilter returns this for every validation failure.
    const err = {
      status: 400,
      error: {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          Name: ['Name is required.'],
          Password: ['Password must be at least 6 characters.', 'Password must contain a digit.'],
        },
        traceId: '00-abc-def-00',
      },
    };

    expect(apiErrorMessage(err, 'fallback')).toBe(
      'Name is required.\nPassword must be at least 6 characters.\nPassword must contain a digit.'
    );
  });

  it('returns a plain string body', () => {
    expect(apiErrorMessage({ status: 400, error: 'Something went wrong.' }, 'fallback')).toBe(
      'Something went wrong.'
    );
  });

  it('parses a Result<T> failure delivered as text', () => {
    // Every Account endpoint posts with responseType: 'text', because the success bodies are
    // plain strings. Angular then leaves the ERROR body unparsed too, so the same
    // IReadOnlyList<Error> arrives as a string and would otherwise be rendered verbatim.
    const err = {
      status: 400,
      error:
        '[{"code":"Auth.TokenInvalid","description":"This link is no longer valid — request a new one."}]',
    };

    expect(apiErrorMessage(err, 'fallback')).toBe(
      'This link is no longer valid — request a new one.'
    );
  });

  it('never renders raw JSON from a text-typed response', () => {
    const err = { status: 400, error: '[{"code":"Auth.TokenInvalid","description":"Expired."}]' };

    expect(apiErrorMessage(err, 'fallback')).not.toContain('"code"');
  });

  it('parses ValidationProblemDetails delivered as text', () => {
    // AcceptInvite and ResetPassword carry FluentValidation validators and are posted as text,
    // so this shape reaches the same string path.
    const err = {
      status: 400,
      error: '{"title":"One or more validation errors occurred.","errors":{"Password":["Password must contain a digit."]}}',
    };

    expect(apiErrorMessage(err, 'fallback')).toBe('Password must contain a digit.');
  });

  it('falls back rather than rendering an HTML error page', () => {
    // A dev-mode 500 returns a stack-trace page as a string body.
    const err = { status: 500, error: '<!DOCTYPE html><html><body>Stack trace</body></html>' };

    expect(apiErrorMessage(err, 'Failed to load.')).toBe('Failed to load.');
  });

  it('reports an unreachable API on status 0', () => {
    expect(apiErrorMessage({ status: 0, error: null }, 'fallback')).toBe(
      'API unreachable / CORS error.'
    );
  });

  it('uses the caller fallback on an empty-bodied 403', () => {
    // The regression that matters: HttpErrorResponse.message is always populated, so any
    // branch reading it makes this fallback unreachable. It must fire here.
    const err = { status: 403, error: null, message: 'Http failure response for /api/x: 403 Forbidden' };

    expect(apiErrorMessage(err, 'You do not have access.')).toBe('You do not have access.');
  });

  it('never surfaces the raw Angular HTTP diagnostic', () => {
    const err = { status: 401, error: null, message: 'Http failure response for /api/x: 401 Unauthorized' };

    expect(apiErrorMessage(err, 'Session expired.')).not.toContain('Http failure response');
  });

  it('tolerates null and undefined', () => {
    expect(apiErrorMessage(null, 'fallback')).toBe('fallback');
    expect(apiErrorMessage(undefined, 'fallback')).toBe('fallback');
  });
});
