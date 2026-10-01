# Local skill registry validation

P0-026 validates the twenty mandatory role manifests and the concise agent entry contract without external retrieval or Product behavior.

    powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerSkillRegistry.ps1 -RepositoryRoot (Get-Location).Path
    powershell.exe -NoProfile -File .\tools\validation\Test-TunnerSkillRegistry.ps1 -RepositoryRoot (Get-Location).Path

Generated context records activated skill paths, semantic versions, and SHA-256 values. Role activation policy and orchestration are separate P0-027 and P0-028 scopes.