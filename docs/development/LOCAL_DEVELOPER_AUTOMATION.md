# Local developer automation

TUN-P0-014 completes the local `tunner-dev` command surface. These commands manage only Tunner’s Compose configuration and its named volumes; they do not deploy a Product service, use a credential, seed Product data, or grant release approval.

From the repository root:

```powershell
./tools/dev/tunner-dev.ps1 doctor
./tools/dev/tunner-dev.ps1 setup
./tools/dev/tunner-dev.ps1 start
./tools/dev/tunner-dev.ps1 health
./tools/dev/tunner-dev.ps1 test
./tools/dev/tunner-dev.ps1 logs
./tools/dev/tunner-dev.ps1 stop
```

`doctor` and `setup` validate Docker Desktop and the checked-in Compose configuration without starting containers. `start`, `stop`, `health`, and `logs` are explicit local Docker operations; run them only when you intend to manage the local dependency environment.

`test` delegates only to `./tools/dev/tunner-test.ps1 unit`. It does not start Docker, install Playwright browsers, access a secret, or run integration/browser profiles. Restore dependencies first if necessary.

## Reset

`reset` deletes the Tunner local Compose containers and named volumes. It is intentionally guarded and is never run by validation:

```powershell
./tools/dev/tunner-dev.ps1 reset -ConfirmReset
```

The reset command uses the repository’s explicit Compose file and project directory. It targets no other Docker project, but it permanently removes Tunner’s local persisted dependency data. Do not use it when that local state is needed.