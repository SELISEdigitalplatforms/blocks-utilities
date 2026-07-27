# Contributing to blocks-utilities

Thank you for your interest in contributing to **blocks-utilities**. Whether you are reporting a bug, suggesting an enhancement, or submitting code changes, your input is welcome.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Reporting Issues](#reporting-issues)
- [Reporting Security Issues](#reporting-security-issues)
- [Branch Model](#branch-model)
- [Commit Guidelines](#commit-guidelines)
- [Before You Open a Pull Request](#before-you-open-a-pull-request)
- [Code Review Process](#code-review-process)
- [License](#license)

## Code of Conduct

Please read and follow our [Code of Conduct](./CODE_OF_CONDUCT.md). By participating in this project, you agree to abide by its terms.

## Reporting Issues

If you encounter a bug or any issue, please open a GitHub issue in this repository and include:

- **Description**: a clear and concise description of the bug
- **Steps to Reproduce**: steps to replicate the issue
- **Expected Behavior**: what should happen
- **Actual Behavior**: what actually happens
- **Screenshots**: if applicable
- **Environment**: OS, browser and versions

## Reporting Security Issues

Do **not** open a public issue for a suspected vulnerability. Follow the private disclosure process in [SECURITY.md](./SECURITY.md).

## Branch Model

- `main`: production-ready code (protected)
- `dev`: integration branch (protected); all pull requests target `dev`
- `inception`: the working branch; day-to-day work happens here

Never commit directly to `dev` or `main`. Work on `inception` and open a pull request from `inception` into `dev`:

```bash
git checkout inception
git pull origin inception
# work, commit
git push origin inception
# then open a PR: inception -> dev
```

Do not force-push and do not rewrite published history.

## Commit Guidelines

This repository uses [Conventional Commits](https://www.conventionalcommits.org/), matching the existing history (for example `test(client): cover auth forms and token modals`):

- Format: `type(scope): subject`, for example `feat(payment): ...`, `fix(mail): ...`, `docs: ...`, `test(client): ...`, `chore: ...`
- Use the imperative mood and keep the subject lowercase
- Keep the subject concise; do not end it with a period
- If more detail is needed, add a body separated by a blank line explaining the what and the why
- Reference related issues in the body (for example `fixes #123`)

## Before You Open a Pull Request

Run the full test suite and make sure your change does not reduce coverage:

```bash
# backend unit tests (no .sln at the repo root, target the csproj)
dotnet test server/XUnitTest/XUnitTest.csproj

# frontend unit tests
npm --prefix client run test

# end-to-end tests (needs a configured e2e/.env.e2e, see e2e/README.md)
npm --prefix e2e run test
```

Security scanning gates apply before merge: SAST, dependency (SCA) and secret scanning must report no new findings. `scripts/scan.sh` is the scan entry point where the scanning environment is available. Fix findings in real code or real dependency versions; do not suppress rules, lower thresholds or delete tests to make a scan pass.

Also:

- Add or update tests for the code you change
- Update `README.md` and any affected docs when behavior or usage changes
- Keep pull requests small and focused
- Never commit secrets, tokens or environment-specific values; `.env` files are gitignored on purpose

## Code Review Process

1. CI runs the build, tests and scans on every pull request into `dev`
2. At least one maintainer must approve the pull request
3. Once approved and green, the pull request is merged into `dev`

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](./LICENSE).
