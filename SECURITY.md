# Security policy

## Supported versions

Only the latest release of IdentityFlow receives security fixes.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately with GitHub's **Report a vulnerability** button on the [Security tab](https://github.com/virtitnerd/IdentityFlow/security/advisories/new) of this repository. This keeps the details private until a fix is available.

Please include:

- what the problem is and what an attacker could do with it
- the version or commit, and the part of the pipeline it affects
- steps to reproduce, or a short description of how you found it

Please do not include real employee or personnel data, passwords, client secrets, or tokens in a report. Redact or replace them first.

This project is maintained by one person, so replies are on a best-effort basis. Confirmed vulnerabilities are fixed as quickly as practical, and you will be credited in the advisory if you would like to be.

## Scope

In scope: the code and infrastructure templates in this repository, including how it handles credentials, secrets, tokens, and identity data.

Out of scope: Paycom, Microsoft Entra ID, Azure, and other third-party services. Please report those to their owners.

## Dependencies

Dependabot security updates are enabled for the NuGet packages and GitHub Actions used here.
