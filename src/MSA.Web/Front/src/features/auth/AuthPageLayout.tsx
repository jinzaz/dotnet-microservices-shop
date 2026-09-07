import type { ReactNode } from 'react';
import { MinimalHeader } from '../../shared/layout/SiteHeader';
import { ServicePromises } from '../../shared/layout/ServicePromises';
import { SiteFooter } from '../../shared/layout/SiteFooter';

export function AuthPageLayout({ children }: { children: ReactNode }) {
  return (
    <div className="login-page">
      <MinimalHeader />
      <main className="login-layout">
        <div className="login-media">
          <img
            src="/images/hero-lifestyle.jpg"
            alt="自然光下的手冲壶、陶瓷杯与绿植"
            width="1672"
            height="941"
          />
        </div>
        <div className="login-panel">{children}</div>
      </main>
      <ServicePromises />
      <SiteFooter />
    </div>
  );
}
