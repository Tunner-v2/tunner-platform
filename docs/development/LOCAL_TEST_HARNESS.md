# Local test harness

`./tools/dev/tunner-test.ps1 unit` is the default safe test command. It does not start Docker or launch a browser.

`integration` requires a local operator to set `TUNNER_RUN_CONTAINER_TESTS=1`; Testcontainers uses a throwaway PostgreSQL descriptor with a random host port. `browser` requires the operator to build the browser test project, install the generated Playwright browsers, and set `TUNNER_RUN_BROWSER_TESTS=1`.

The contract-test directory is intentionally empty of Product contracts until a governing contract is approved. No profile targets production.