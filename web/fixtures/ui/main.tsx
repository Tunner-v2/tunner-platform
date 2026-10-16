import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ContentProvider, ThemeProvider, TextField, PrimaryButton, TunnerBrand } from '@tunner/ui';
import type { ThemeSurface, PublishedContentResolver } from '@tunner/ui';
import './fixture.css';

// Development-only publication snapshots. This file is not a Product content service or login workflow.
const labels: Record<string, Record<ThemeSurface, string>> = {
  brand: { user: 'Tunner', admin: 'Tunner' },
  action: { user: 'Sign in', admin: 'Review product setup →' },
  field: { user: 'Email', admin: 'Stripe Account ID' },
  help: { user: 'Development example. No account request is sent.', admin: 'Development example. No provider request is sent.' },
  error: { user: 'Enter the value requested by the owning screen.', admin: 'Enter the value requested by the owning screen.' },
  disabled: { user: 'Disabled example', admin: 'Disabled example' },
  long: { user: 'Review this deliberately long development label to verify text wrapping at narrow widths', admin: 'Review this deliberately long development label to verify text wrapping at narrow widths' },
};
function Panel({ surface }: { surface: ThemeSurface }) {
  const [value, setValue] = useState(surface === 'user' ? 'misha@example.test' : 'acct_••••91');
  const [count, setCount] = useState(0); const [version, setVersion] = useState(1);
  const resolver: PublishedContentResolver = (key, context) => {
    const text = labels[key]?.[context.surface];
    if (!text) return undefined;
    return { key, locale: context.locale, state: 'PUBLISHED', variantRef: `fixture-${context.surface}`, versionRef: `fixture-v${version}`, publicationRef: 'development-fixture-only', text: key === 'action' && version > 1 ? `Updated ${text}` : text };
  };
  return <ThemeProvider surface={surface}><ContentProvider locale="en" audience="development-verification" resolve={resolver}>
    <section aria-labelledby={`${surface}-title`} className="verification-panel" data-testid={`${surface}-panel`}>
      <h2 id={`${surface}-title`}>{surface === 'user' ? 'User light reference' : 'Admin light reference'}</h2>
      <div className="verification-stack"><TunnerBrand contentKey="brand" />
        <form onSubmit={event => { event.preventDefault(); setCount(current => current + 10); }}>
          <div className="reference-width"><TextField id={`${surface}-field`} labelKey="field" value={value} onChange={event => setValue(event.target.value)} /></div>
          <div className={surface === 'user' ? 'reference-width' : 'admin-reference-action'}><PrimaryButton labelKey="action" onClick={() => setCount(current => current + 1)} /></div>
          <TextField id={`${surface}-error`} labelKey="field" helpKey="help" errorKey="error" value="" readOnly />
          <PrimaryButton labelKey="disabled" disabled onClick={() => setCount(current => current + 100)} />
          <PrimaryButton labelKey="long" />
        </form>
        <button className="verification-utility bg-tunner-surface text-tunner-text" type="button" onClick={() => setVersion(current => current + 1)}>Refresh fixture publication</button>
        <output className="verification-output" data-testid={`${surface}-count`} aria-label="Fixture action count">{count}</output>
      </div>
    </section>
  </ContentProvider></ThemeProvider>;
}
function NestedIsolation() {
  const resolve: PublishedContentResolver = (key, context) => ({key,locale:context.locale,state:'PUBLISHED',variantRef:context.surface,versionRef:'fixture-v1',publicationRef:'development-fixture-only',text:context.surface === 'user' ? 'Outer User example' : 'Nested Admin example'});
  return <ThemeProvider surface="user"><ContentProvider locale="en" audience="development-verification" resolve={resolve}><section aria-label="Nested theme verification" data-testid="nested-themes" className="verification-stack">
    <PrimaryButton labelKey="outer"/><ThemeProvider surface="admin"><PrimaryButton labelKey="inner"/></ThemeProvider><PrimaryButton labelKey="outer-again"/>
  </section></ContentProvider></ThemeProvider>;
}
function App() { return <main className="verification-page"><h1>Shared UI verification</h1><p>Development fixture for approved light profiles. Controls have no sign-in or provider behavior.</p><div className="verification-panels"><Panel surface="user"/><Panel surface="admin"/></div><NestedIsolation/></main>; }
const root = document.getElementById('root');
if (!root) throw new Error('Fixture root missing');
createRoot(root).render(<App/>);
