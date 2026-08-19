# Published integration documentation

The API serves this directory under `/docs` independently of the React build output.

## Subscription guide URLs

- Current: `/docs/subscription/`
- Version 1: `/docs/subscription/v1/`

Share the current URL with clients. It redirects to the current major version. For a breaking
contract, copy the guide into a new version directory, update `subscription/index.html`, and keep
the earlier directory available to existing integrations.

Corrections to the existing contract can be made in place. Documentation responses use
`Cache-Control: no-cache, must-revalidate`, so browsers check for updates after deployment.
