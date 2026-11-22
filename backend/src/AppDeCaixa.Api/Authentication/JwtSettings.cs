namespace AppDeCaixa.Api.Authentication;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "AppDeCaixa";
    public string Audience { get; set; } = "AppDeCaixa.Client";
    public int LifetimeMinutes { get; set; } = 15;
}
