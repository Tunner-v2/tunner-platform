import {defineConfig} from 'vite';
import {fileURLToPath} from 'node:url';
export default defineConfig({root:fileURLToPath(new URL('.',import.meta.url)),base:'/browser-testing/recovery-private-assets/',server:{host:'127.0.0.1',port:5289,strictPort:true},build:{outDir:'dist',emptyOutDir:true}});
