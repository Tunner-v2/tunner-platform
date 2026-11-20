import {defineConfig} from 'vite';
import {fileURLToPath} from 'node:url';
export default defineConfig({root:fileURLToPath(new URL('.',import.meta.url)),base:'/browser-testing/recovery-codes-assets/',server:{host:'127.0.0.1',port:5239,strictPort:true},build:{outDir:'dist',emptyOutDir:true}});
