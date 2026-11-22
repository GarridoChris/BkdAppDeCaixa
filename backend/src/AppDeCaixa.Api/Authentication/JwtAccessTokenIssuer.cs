using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AppDeCaixa.Application.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppDeCaixa.Api.Authentication;

public sealed class JwtAccessTokenIssuer(
    IOptions<JwtSettings> options,
    SigningCredentials signingCredentials) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(AuthenticatedUser user)
    {
        var settings = options.Value;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(settings.LifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.CompanyId is not null)
        {
            claims.Add(new Claim("company_id", user.CompanyId.Value.ToString()));
        }

        if (user.EmployeeId is not null)
        {
            claims.Add(new Claim("employee_id", user.EmployeeId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signingCredentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
