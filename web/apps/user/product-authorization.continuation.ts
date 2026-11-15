/** Resume only after the existing guard has confirmed an actual authenticated User session. */
export async function resumeProductAuthorization(csrfToken: string, signal: AbortSignal): Promise<void> {
  const response = await fetch('/product-authorization/continuation/resume', {
    method: 'POST', credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal,
    headers: { 'Content-Type': 'application/json', 'X-Tunner-CSRF': csrfToken }, body: '{}'
  });
  if (!response.ok) throw new Error('Product continuation unavailable');
  const result: unknown = await response.json();
  if (!result || typeof result !== 'object' || !('resumePath' in result) || typeof result.resumePath !== 'string' ||
      !/^\/[A-Za-z0-9_-]+(?:\/[A-Za-z0-9_-]+)*$/.test(result.resumePath)) throw new Error('Invalid Product continuation');
  const target = new URL(result.resumePath, window.location.origin);
  if (target.origin !== window.location.origin || target.pathname !== result.resumePath || target.search || target.hash)
    throw new Error('Invalid Product continuation');
  signal.throwIfAborted();
  // A document request re-enters native OAuth validation; SPA state cannot issue a grant.
  window.location.assign(target.pathname);
}
