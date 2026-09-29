# Tunner Repository Blueprint — Pre-P0

```text
/
  AGENTS.md
  README.md
  global.json
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  .gitignore
  .agent/skills/<role>/SKILL.md
  docs/authority/{baseline,amendments,decisions}/
  docs/authority/authority-manifest.yaml
  docs/context/current/
  docs/project/{PROJECT_CONTEXT,CURRENT_STATE,DECISION_INDEX,RISK_REGISTER,HANDOFF}.md
  governance/{schemas,milestones,sprints,work-items,todos,defects,reopens,gates,reviews,evidence,releases,bootstrap}/
  tools/Tunner.Tooling.sln
  tools/src/{Tunner.Cli,Tunner.Governance,Tunner.Context,Tunner.Orchestrator,Tunner.Authority}/
  tools/tests/
  src/Tunner.Platform.sln
  src/{Api,Worker,Modules,Bff,Frontend}/
  tests/{Unit,Integration,Contracts,Architecture,Ui,Security}/
  infra/{docker,observability,openbao,scripts}/
  .github/{CODEOWNERS,pull_request_template.md,workflows/}
```
Governance tooling is outside the production solution. `docs/authority/` is a hash-verified execution mirror of approved Drive authority.
