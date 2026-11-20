/** Resume only after the existing guard has confirmed an actual authenticated User session. */
export async function resumeProductAuthorization(csrfToken: string, signal: AbortSignal): Promise<void> {
  const response = await fetch('/product-authorization/continuation/resume', {
    method: 'POST', credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal,
    headers: { 'Content-Type': 'application/json', 'X-Tunner-CSRF': csrfToken }, body: '{}'
  });
  if (!response.ok) throw new Error('Product continuation unavailable');
  const result: unknown = await response.json();
  if (!result || typeof result !== 'object' || !('resumePath' in result)) throw new Error('Invalid Product continuation');
  followProductAuthorizationPath(result.resumePath, signal);
}

export function followProductAuthorizationPath(path: unknown, signal: AbortSignal): void {
  if (typeof path !== 'string' || !/^\/[A-Za-z0-9_-]+(?:\/[A-Za-z0-9_-]+)*$/.test(path)) throw new Error('Invalid Product continuation');
  const target = new URL(path, window.location.origin);
  if (target.origin !== window.location.origin || target.pathname !== path || target.search || target.hash)
    throw new Error('Invalid Product continuation');
  signal.throwIfAborted();
  // A document request re-enters native OAuth validation; SPA state cannot issue a grant.
  window.location.assign(target.pathname);
}
