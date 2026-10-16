import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
export default defineConfig({ build: { outDir: fileURLToPath(new URL('./dist', import.meta.url)), emptyOutDir: false,
  lib: { entry: fileURLToPath(new URL('./contracts.test.tsx', import.meta.url)), formats: ['es'], fileName: 'contracts.test' },
  rolldownOptions: { external: (id: string) => id.startsWith('node:') || id === 'react' || id.startsWith('react/') || id.startsWith('react-dom/') }
} });
