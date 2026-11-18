using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MiniToolsHub.Server.Api;

/// <summary>Endpoint filter: Authorization: Bearer &lt;token&gt;, compared by SHA-256 in constant time.</summary>
public sealed class ApiKeyAuth(IOptions<HubOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!IsAuthorized(context.HttpContext.Request.Headers.Authorization.ToString(), options.Value.AdminTokenSha256))
        {
            context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
            return Results.Json(new { error = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        return await next(context);
    }

    public static bool IsAuthorized(string authorizationHeader, string expectedSha256Hex)
    {
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var token = authorizationHeader[prefix.Length..].Trim();
        if (token.Length == 0 || string.IsNullOrWhiteSpace(expectedSha256Hex)) return false;

        byte[] expected;
        try { expected = Convert.FromHexString(expectedSha256Hex.Trim()); }
        catch (FormatException) { return false; }
        if (expected.Length != 32) return false;

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
