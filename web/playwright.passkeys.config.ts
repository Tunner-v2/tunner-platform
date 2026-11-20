import {defineConfig} from '@playwright/test';
export default defineConfig({outputDir:'test-results/runs/passkeys',testDir:'./tests/passkeys',fullyParallel:false,workers:1,reporter:[['list'],['json',{outputFile:'test-results/passkeys/results.json'}]],
  use:{baseURL:'http://127.0.0.1:5193',viewport:{width:1440,height:1100},channel:'msedge'},
  webServer:{command:'node node_modules/vite/bin/vite.js --config apps/user/vite.config.ts --port 5193',url:'http://127.0.0.1:5193/registration/',reuseExistingServer:false,timeout:30000}});
