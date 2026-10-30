using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using ProjectName.ControlPlane;

namespace ProjectName.OrchestrationApi;

/// <summary>
/// Development-only Windows Negotiate identity inspection. This is not an
/// activation, grant issuer, Checker, or adapter registered with AdmissionGate.
/// Only an authentication result from the configured Negotiate handler is
/// accepted; client-supplied names, headers and arbitrary ClaimsPrincipal
/// instances are insufficient.
/// </summary>
public static class WindowsDevelopmentIdentityEndpoint
{
    public const string Route = "/internal/dev/windows-identity";

    public static bool MayMap(IHostEnvironment environment) =>
        environment.IsDevelopment() && OperatingSystem.IsWindows();

    public static VerifiedIdentity? Resolve(AuthenticateResult authentication)
    {
        if (!OperatingSystem.IsWindows() ||
            !authentication.Succeeded ||
            authentication.Ticket?.AuthenticationScheme != NegotiateDefaults.AuthenticationScheme ||
            authentication.Principal?.Identity is not WindowsIdentity identity ||
            !identity.IsAuthenticated ||
            identity.User is not SecurityIdentifier sid ||
            string.IsNullOrWhiteSpace(sid.Value))
            return null;

        // A local Windows SID is an authenticated development identity, not
        // evidence of a PMCR-O role, tenant grant or independent Checker PASS.
        return new VerifiedIdentity(
            TenantId: "projectname-windows-development",
            ActorId: sid.Value,
            IssuerId: "windows-negotiate-local");
    }

    public static async Task<IResult> ProbeAsync(HttpContext context)
    {
        if (!MayMap(context.RequestServices.GetRequiredService<IHostEnvironment>()) ||
            context.Connection.RemoteIpAddress is not IPAddress remote ||
            !IPAddress.IsLoopback(remote))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // Browser requests from other sites cannot initiate an identity probe.
        string fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (!string.IsNullOrEmpty(fetchSite) &&
            fetchSite is not ("none" or "same-origin"))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // Always invoke the real registered handler. Never infer identity from
        // HttpContext.User, X-Forwarded-User, or a request-supplied SID.
        AuthenticateResult authentication =
            await context.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);

        if (!authentication.Succeeded)
            return Results.Challenge(
                authenticationSchemes: [NegotiateDefaults.AuthenticationScheme]);

        VerifiedIdentity? identity = Resolve(authentication);
        if (identity is null)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // Do not disclose the Windows username or SID. The response is
        // descriptive only and intentionally cannot perform admission.
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity.ActorId)))[..16];

        return Results.Ok(new
        {
            mode = "DEVELOPMENT_IDENTITY_INSPECTION_ONLY",
            provider = "WindowsNegotiate",
            verified = true,
            actorFingerprint = fingerprint,
            grantIssued = false,
            independentCheckerPass = false,
            workerAdmission = "deny-all",
            mcpToolExecutionAuthorized = false
        });
    }
}
