import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./tests/content-admin',outputDir:'test-results/runs/content-admin',workers:1,fullyParallel:false,
 reporter:[['list'],['json',{outputFile:'test-results/content-admin/results.json'}]],
 use:{baseURL:'http://127.0.0.1:5297',channel:'msedge'},
 webServer:{command:'node node_modules/vite/bin/vite.js --config apps/admin/vite.config.ts',url:'http://127.0.0.1:5297/admin/content',reuseExistingServer:false,timeout:30000}});
