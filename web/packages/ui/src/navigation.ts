import { useSyncExternalStore } from 'react';
const changedEvent = 'tunner:pathname-changed';
const serverLocation = { pathname:'/', revision:0 };
let current = serverLocation;
const listeners = new Set<() => void>();
function synchronize() { current={pathname:window.location.pathname,revision:current.revision+1};listeners.forEach(changed=>changed()); }
function subscribe(changed: () => void) {
  listeners.add(changed);
  if(listeners.size===1){window.addEventListener('popstate',synchronize);window.addEventListener(changedEvent,synchronize);}
  return()=>{listeners.delete(changed);if(listeners.size===0){window.removeEventListener('popstate',synchronize);window.removeEventListener(changedEvent,synchronize);}};
}
function snapshot(){if(current.pathname!==window.location.pathname)current={pathname:window.location.pathname,revision:current.revision};return current;}
/** Primary navigation accepts a local pathname only; owners must guard every navigation revision. */
export function navigatePath(pathname: string, replace = false) {
  if (!pathname.startsWith('/') || pathname.startsWith('//') || /[?#\\]/.test(pathname) || pathname.split('/').some(part => part === '.' || part === '..'))
    throw new Error('Primary navigation requires a safe local pathname');
  const target = new URL(pathname, window.location.origin);
  if (target.origin !== window.location.origin || target.pathname !== pathname) throw new Error('Invalid primary pathname');
  if (window.location.pathname === pathname && !window.location.search && !window.location.hash) return;
  window.history[replace ? 'replaceState' : 'pushState'](null, '', pathname);
  window.dispatchEvent(new Event(changedEvent));
}
export function useRouteLocation() { return useSyncExternalStore(subscribe,snapshot,()=>serverLocation); }
export function usePathname() { return useRouteLocation().pathname; }
