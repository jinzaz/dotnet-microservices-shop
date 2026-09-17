import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

/** Route changes start at the top unless the URL targets an anchor. */
export function ScrollToTop() {
  const { pathname, hash } = useLocation();
  useEffect(() => {
    if (!hash) window.scrollTo({ top: 0 });
  }, [pathname, hash]);
  return null;
}
