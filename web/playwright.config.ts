import { defineConfig } from '@playwright/test';
export default defineConfig({ outputDir:'test-results/runs/shared', testDir: './tests/browser', fullyParallel: false, workers: 1,
  reporter: [['list'], ['json', { outputFile: 'test-results/results.json' }]],
  use: { baseURL: 'http://127.0.0.1:5188', viewport: { width: 1024, height: 1000 }, channel: 'msedge' },
  webServer: { command: 'node node_modules/vite/bin/vite.js --config fixtures/ui/vite.config.ts', url: 'http://127.0.0.1:5188', reuseExistingServer: false, timeout: 30000 }
});
