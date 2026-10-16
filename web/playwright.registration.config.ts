import { defineConfig } from '@playwright/test';
export default defineConfig({testDir:'./tests/registration',fullyParallel:false,workers:1,reporter:[['list'],['json',{outputFile:'test-results/registration/results.json'}]],
  use:{baseURL:'http://127.0.0.1:5189',viewport:{width:1440,height:1024},channel:'msedge'},
  webServer:{command:'node node_modules/vite/bin/vite.js --config apps/user/vite.config.ts',url:'http://127.0.0.1:5189/registration/',reuseExistingServer:false,timeout:30000}});
