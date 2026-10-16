import { createContext, useContext } from 'react';
import type { ReactNode } from 'react';
import { themeSurfaces, themeModes } from './theme.generated';
import type { ThemeSurface, ThemeMode } from './theme.generated';

const ThemeContext = createContext<ThemeSurface | null>(null);
export interface ThemeProviderProps { surface: ThemeSurface; mode?: ThemeMode; children: ReactNode }
export function ThemeProvider({ surface, mode = 'light', children }: ThemeProviderProps) {
  if (!(themeSurfaces as readonly string[]).includes(surface) || !(themeModes as readonly string[]).includes(mode)) throw new Error('Unsupported Tunner theme');
  return <ThemeContext.Provider value={surface}><div className="tn-theme" data-tunner-surface={surface} data-tunner-theme={mode}>{children}</div></ThemeContext.Provider>;
}
export function useThemeSurface(): ThemeSurface {
  const surface = useContext(ThemeContext);
  if (surface === null) throw new Error('Tunner controls require ThemeProvider');
  return surface;
}
