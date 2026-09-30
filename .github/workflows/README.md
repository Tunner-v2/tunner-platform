# GitHub Actions workflows

## `automate-pr-automerge.yml`

This workflow enables GitHub's native auto-merge for an eligible, non-draft pull request opened from a branch in this repository. GitHub performs the merge only after the repository ruleset has passed, including the required `CODEOWNERS` approval and any required status checks.

The workflow never submits a review, approves a pull request, bypasses the ruleset, or checks out and executes pull-request code. Fork pull requests are excluded deliberately.

### Repository-owner setup

Before the first run, enable **Allow auto-merge** under **Settings → General → Pull Requests**. If organizational Actions policy restricts the token, also permit the workflow's requested `contents: write` and `pull-requests: write` permissions.

### Validation

After this workflow is merged to `main`, open a non-draft same-repository pull request. Confirm that the workflow succeeds and the pull request shows auto-merge enabled, remains blocked until a code-owner review is approved, and merges only after all protected-branch requirements pass.

If auto-merge is not allowed by repository settings or policy, the workflow fails visibly and the pull request remains unmerged.

## `create-pull-request.yml`

This workflow creates a pull request to `main` whenever a non-`main` branch is pushed and does not already have an open pull request. It uses an explicit branch-based title and body, so it does not need to check out repository code. It has only `contents: read` and `pull-requests: write` permissions.

It does not approve, merge, bypass the ruleset, access repository secrets, or execute repository code. The required `CODEOWNERS` review remains mandatory; the companion auto-merge workflow handles the merge only after that review and all other protected-branch requirements pass.

### One-time repository-owner setting

Under **Settings → Actions → General → Workflow permissions**, select **Read and write permissions** and enable **Allow GitHub Actions to create and approve pull requests**. The latter is needed only so GitHub Actions can create a pull request; this workflow never submits an approval.
