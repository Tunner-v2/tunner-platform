import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
export default defineConfig({root:fileURLToPath(new URL('.',import.meta.url)),base:'/browser-testing/step-up-assets/',server:{host:'127.0.0.1',port:5192,strictPort:true},build:{outDir:'dist',emptyOutDir:true}});
