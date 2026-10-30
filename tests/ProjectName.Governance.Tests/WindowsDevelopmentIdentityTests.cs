using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using ProjectName.OrchestrationApi;

public sealed class WindowsDevelopmentIdentityTests
{
    [Fact]
    public void NoAuthenticationDoesNotResolveIdentity()
    {
        Assert.Null(WindowsDevelopmentIdentityEndpoint.Resolve(AuthenticateResult.NoResult()));
    }

    [Fact]
    public void CallerFabricatedNegotiateClaimsDoNotResolveIdentity()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "FAKE\\user"), new Claim(ClaimTypes.NameIdentifier, "S-1-5-18")],
            NegotiateDefaults.AuthenticationScheme));
        var ticket = new AuthenticationTicket(
            claims, new AuthenticationProperties(), NegotiateDefaults.AuthenticationScheme);

        Assert.Null(WindowsDevelopmentIdentityEndpoint.Resolve(AuthenticateResult.Success(ticket)));
    }

    [Fact]
    public void WrongAuthenticationSchemeDoesNotResolveIdentity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "some-user")], "FakeCookie"));
        var ticket = new AuthenticationTicket(principal, "FakeCookie");

        Assert.Null(WindowsDevelopmentIdentityEndpoint.Resolve(AuthenticateResult.Success(ticket)));
    }

    [Fact]
    public void NotAuthenticatedNegotiateIdentityDoesNotResolveIdentity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var ticket = new AuthenticationTicket(principal, NegotiateDefaults.AuthenticationScheme);

        Assert.Null(WindowsDevelopmentIdentityEndpoint.Resolve(AuthenticateResult.Success(ticket)));
    }
}
