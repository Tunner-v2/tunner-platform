import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./tests/configuration-admin',outputDir:'test-results/runs/configuration-admin',workers:1,fullyParallel:false,
 reporter:[['list'],['json',{outputFile:'test-results/configuration-admin/results.json'}]],use:{baseURL:'http://127.0.0.1:5298',channel:'msedge'},
 webServer:{command:'node node_modules/vite/bin/vite.js --config apps/admin/vite.config.ts --port 5298',url:'http://127.0.0.1:5298/identity/settings/authentication',reuseExistingServer:false,timeout:30000}});
