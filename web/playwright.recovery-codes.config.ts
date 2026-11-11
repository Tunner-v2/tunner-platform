import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./tests/recovery-codes/ui',outputDir:'test-results/runs/recovery-codes-public-ui',fullyParallel:false,workers:1,retries:0,reporter:[['list'],['json',{outputFile:'test-results/recovery-codes-public-ui/results.json'}]],
 use:{baseURL:'http://127.0.0.1:5239',viewport:{width:1440,height:1100},channel:'msedge',trace:'off',video:'off',screenshot:'off',acceptDownloads:false},
 webServer:{command:'node node_modules/vite/bin/vite.js --config fixtures/recovery-codes/vite.config.ts',url:'http://127.0.0.1:5239/browser-testing/recovery-codes-assets/',reuseExistingServer:process.env.TUNNER_RECOVERY_REUSE_FIXTURES==='1',timeout:30000}});
