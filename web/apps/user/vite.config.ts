import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
export default defineConfig({ root: fileURLToPath(new URL('.', import.meta.url)), base: '/registration/',
  plugins:[{name:'canonical-registration-dev-entry',configureServer(server){server.middlewares.use((request,_response,next)=>{if(request.url==='/login'||request.url==='/signed-in'){request.url='/registration/authentication.html';}if(request.url==='/registration'||request.url?.startsWith('/registration?'))request.url=request.url.replace('/registration','/registration/');next();});}}],
  server: { host: '127.0.0.1', port: 5189, strictPort: true }, build: { outDir: 'dist', emptyOutDir: true, rollupOptions:{input:{registration:fileURLToPath(new URL('./index.html',import.meta.url)),authentication:fileURLToPath(new URL('./authentication.html',import.meta.url))}} } });
