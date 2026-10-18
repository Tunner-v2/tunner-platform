import { defineConfig } from '@playwright/test';
import { readFileSync } from 'node:fs';
const state=JSON.parse(readFileSync('../artifacts/browser-testing/runtime.json','utf8').replace(/^\uFEFF/,''));
export default defineConfig({testDir:'./tests/browser-testing',workers:1,reporter:[['list'],['json',{outputFile:'../artifacts/browser-testing/browser-results.json'}]],outputDir:'../artifacts/browser-testing/browser-results',use:{baseURL:'https://localhost:'+state.ApplicationPort,viewport:{width:1440,height:1024},channel:'msedge',ignoreHTTPSErrors:true}});
