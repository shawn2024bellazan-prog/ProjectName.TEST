# Windows Integrated Authentication — development identity probe

Status: DEVELOPMENT CANDIDATE. This file does not approve an MCP allowlist,
grant any capability, or authorize a PMCR-O worker.

The Orchestration API registers the Windows Negotiate authentication handler
only when the process runs on Windows in the Development environment.
The read-only endpoint is:

    GET /internal/dev/windows-identity

Its route is absent in Production. The handler rejects non-loopback clients,
cross-site browser fetches, failed Negotiate authentication, and identities
that are not backed by a WindowsIdentity with a valid SID. It accepts no
caller-supplied identity headers. Its success response contains only a short
SHA-256 fingerprint of the Windows SID (not the SID or username), a
development marker, and explicit deny-all admission flags.

The endpoint does not register IVerifiedIdentityResolver with AdmissionGate
and does not change DenyAllAuthorityRepository, DenyAllReceiptVerifier, MCP
tool policy, capability permissions, revocation, or Checker status.

## Usage on the development Windows host

1. Run the API directly on localhost under Development, or use the existing
   Aspire AppHost's direct API endpoint (not a reverse proxy). Negotiate is
   connection-oriented and must not traverse a proxy lacking 1:1 connection
   affinity. Do not configure anonymous impersonation or store credentials.
2. Request the endpoint with Windows default credentials using a trusted
   Windows-native HTTP client. Browsers may require an explicit intranet
   authentication policy for loopback URLs.
3. Verify a 200 authenticated result, 401 challenge for anonymous requests,
   and 403 for cross-site or non-loopback requests.
4. Verify /v1/cycles still denies admission and production environment
   exposes no development identity route.

## Remaining approval prerequisites

Require an authenticated and authorized host approval transaction specifying
the exact tool allowlist, approved Windows principal (SID or group), scopes,
repository/source digest, expiration, revocation policy and a separately
authenticated independent Checker verdict. Human approval must be verified
per invocation before any side-effecting tool is executed. Until these
conditions are satisfied: Filesystem, Playwright and Terminal MCP servers
remain unported and production worker admission stays DENY.

This probe is identity verification only and is not itself authority.
