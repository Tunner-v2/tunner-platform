import { createContext, useContext } from 'react';
import type { ReactNode } from 'react';
import { useThemeSurface } from './theme';
import type { ThemeSurface } from './theme.generated';

export interface PublishedContent {
  key: string; variantRef: string; versionRef: string; publicationRef: string;
  locale: string; state: 'PUBLISHED'; text: string;
}
export interface ContentContext { surface: ThemeSurface; locale: string; audience: string }
export type PublishedContentResolver = (key: string, context: Readonly<ContentContext>) => Readonly<PublishedContent> | undefined;
export interface ContentProviderProps { locale: string; audience: string; resolve: PublishedContentResolver; children: ReactNode }
const Content = createContext<Omit<ContentProviderProps, 'children'> | null>(null);
const present = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0;
export function ContentProvider({ locale, audience, resolve, children }: ContentProviderProps) {
  if (!present(locale) || !present(audience) || typeof resolve !== 'function') throw new Error('Explicit content context required');
  return <Content.Provider value={{ locale, audience, resolve }}>{children}</Content.Provider>;
}
export function usePublishedContent(key: string): Readonly<PublishedContent> {
  const provider = useContext(Content);
  const surface = useThemeSurface();
  if (!provider || !present(key)) throw new Error('Required published content key/provider missing');
  const value = provider.resolve(key, Object.freeze({ surface, locale: provider.locale, audience: provider.audience }));
  if (!value || value.key !== key || value.locale !== provider.locale || value.state !== 'PUBLISHED' ||
      ![value.variantRef, value.versionRef, value.publicationRef, value.text].every(present)) throw new Error(`Invalid published content: ${key}`);
  return value;
}
export function contentProvenance(value: Readonly<PublishedContent>) {
  return { 'data-content-key': value.key, 'data-content-variant': value.variantRef, 'data-content-version': value.versionRef, 'data-content-publication': value.publicationRef, lang: value.locale };
}
export function ContentText({ contentKey }: { contentKey: string }) {
  const value = usePublishedContent(contentKey);
  return <span {...contentProvenance(value)}>{value.text}</span>;
}
