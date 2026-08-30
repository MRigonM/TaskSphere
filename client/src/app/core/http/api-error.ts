/**
 * Turns a failed HTTP response into something worth showing a user.
 *
 * The API returns exactly two error shapes:
 *   1. `ApiBaseController.MapErrors` serialises `IReadOnlyList<Error>`, and `Error` is
 *      `record Error(string Code, string Description)` — so the body is
 *      `[{ code, description }]`.
 *   2. FluentValidation and model binding fail through `[ApiController]`'s automatic 400,
 *      which returns `ValidationProblemDetails` — `{ errors: { Field: ["msg"] } }`.
 *
 * `err.message` is deliberately never consulted. Angular always populates
 * `HttpErrorResponse.message` with "Http failure response for <url>: <status> <text>", so any
 * branch reading it is unconditionally truthy and makes the caller's fallback unreachable.
 */
export function apiErrorMessage(err: unknown, fallback: string): string {
  const e = err as any;

  // 1. Result<T> failure.
  if (Array.isArray(e?.error)) {
    const descriptions = e.error
      .map((x: any) => (typeof x === 'string' ? x : x?.description))
      .filter((d: unknown): d is string => typeof d === 'string' && d.trim().length > 0);

    if (descriptions.length) return descriptions.join('\n');
  }

  // 2. ValidationProblemDetails.
  const fieldErrors = e?.error?.errors;
  if (fieldErrors && typeof fieldErrors === 'object' && !Array.isArray(fieldErrors)) {
    const messages = Object.values(fieldErrors)
      .flatMap((v: unknown) => (Array.isArray(v) ? v : [v]))
      .filter((m: unknown): m is string => typeof m === 'string' && m.trim().length > 0);

    if (messages.length) return messages.join('\n');
  }

  // 3. A plain string body — but never an HTML error page.
  if (typeof e?.error === 'string') {
    const body = e.error.trim();
    if (body.length > 0 && !body.startsWith('<')) return body;
  }

  // 4. The request never reached the API.
  if (e?.status === 0) return 'API unreachable / CORS error.';

  return fallback;
}
