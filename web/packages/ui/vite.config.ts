import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
export default defineConfig({
  build: { outDir: fileURLToPath(new URL('./dist', import.meta.url)), emptyOutDir: false,
    lib: { entry: fileURLToPath(new URL('./src/index.ts', import.meta.url)), formats: ['es'], fileName: 'tunner-ui' },
    rolldownOptions: { external: ['react', 'react/jsx-runtime'] }
  }
});
