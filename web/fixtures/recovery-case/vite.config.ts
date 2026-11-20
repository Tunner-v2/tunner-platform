import {defineConfig} from 'vite';
import {fileURLToPath} from 'node:url';
export default defineConfig({root:fileURLToPath(new URL('.',import.meta.url)),base:'/registration/',publicDir:fileURLToPath(new URL('../../apps/user/public',import.meta.url)),server:{host:'127.0.0.1',port:5191,strictPort:true},build:{outDir:'dist',emptyOutDir:true}});
