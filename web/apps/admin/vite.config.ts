import {defineConfig} from 'vite';
import tailwindcss from '@tailwindcss/vite';
import {fileURLToPath} from 'node:url';
import {isContentRoute} from './content.routes';
import {configurationRoute} from './configuration.routes';
export default defineConfig({root:fileURLToPath(new URL('.',import.meta.url)),base:'/admin-assets/',plugins:[tailwindcss(),{name:'canonical-admin-content-entry',configureServer(server){server.middlewares.use((request,_response,next)=>{const path=request.url?.split('?')[0]??'';if(isContentRoute(path))request.url='/admin-assets/content.html';else if(configurationRoute(path))request.url='/admin-assets/configuration.html';next();});}}],
  resolve:{alias:[{find:'@tunner/ui/styles.css',replacement:fileURLToPath(new URL('../../packages/ui/styles/index.css',import.meta.url))},{find:'@tunner/ui',replacement:fileURLToPath(new URL('../../packages/ui/src/index.ts',import.meta.url))}]},
  server:{host:'127.0.0.1',port:5297,strictPort:true},build:{outDir:'dist',emptyOutDir:true,rollupOptions:{input:{content:fileURLToPath(new URL('./content.html',import.meta.url)),configuration:fileURLToPath(new URL('./configuration.html',import.meta.url))}}}});
