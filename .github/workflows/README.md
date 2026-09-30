# GitHub Actions workflows

## `automate-pr-automerge.yml`

This workflow enables GitHub's native auto-merge for an eligible, non-draft pull request opened from a branch in this repository. GitHub performs the merge only after the repository ruleset has passed, including the required `CODEOWNERS` approval and any required status checks.

The workflow never submits a review, approves a pull request, bypasses the ruleset, or checks out and executes pull-request code. Fork pull requests are excluded deliberately.

### Repository-owner setup

Before the first run, enable **Allow auto-merge** under **Settings → General → Pull Requests**. If organizational Actions policy restricts the token, also permit the workflow's requested `contents: write` and `pull-requests: write` permissions.

### Validation

After this workflow is merged to `main`, open a non-draft same-repository pull request. Confirm that the workflow succeeds and the pull request shows auto-merge enabled, remains blocked until a code-owner review is approved, and merges only after all protected-branch requirements pass.

If auto-merge is not allowed by repository settings or policy, the workflow fails visibly and the pull request remains unmerged.
