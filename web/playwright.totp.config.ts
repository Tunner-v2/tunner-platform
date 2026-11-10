import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./tests/totp/ui',outputDir:'test-results/runs/totp-public-ui',fullyParallel:false,workers:1,reporter:[['list'],['json',{outputFile:'test-results/totp-public-ui/results.json'}]],
 use:{baseURL:'http://127.0.0.1:5194',viewport:{width:1440,height:1100},channel:'msedge',trace:'off',video:'off',screenshot:'off'},
 webServer:[{command:'node node_modules/vite/bin/vite.js --config apps/user/vite.config.ts --port 5194',url:'http://127.0.0.1:5194/registration/',reuseExistingServer:process.env.TUNNER_TOTP_REUSE_FIXTURES==='1',timeout:30000},
 {command:'node node_modules/vite/bin/vite.js --config fixtures/step-up/vite.config.ts --port 5195',url:'http://127.0.0.1:5195/browser-testing/step-up-assets/',reuseExistingServer:process.env.TUNNER_TOTP_REUSE_FIXTURES==='1',timeout:30000}]});
