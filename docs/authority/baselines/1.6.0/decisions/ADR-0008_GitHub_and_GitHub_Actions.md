# ADR-0008 — Git Hosting and CI/CD: GitHub + GitHub Actions

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
Use GitHub for source hosting and GitHub Actions for CI/CD. Enforce Rulesets, protected `main`, CODEOWNERS, required status/security checks, reusable workflows, OIDC for cloud credentials, release tags, artifact attestations/SBOM linkage and governed evidence.

## Boundaries
GitHub is workflow infrastructure; Tunner repository-native governance records remain the authoritative development-state model. Environment state is not represented by long-lived environment branches.

## Revisit trigger
Organizational/compliance requirements make another host mandatory.
