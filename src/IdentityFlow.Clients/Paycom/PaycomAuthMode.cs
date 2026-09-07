namespace IdentityFlow.Clients.Paycom;

public enum PaycomAuthMode
{
    /// <summary>SID + API token sent as HTTP Basic Authentication (SID as username, token as password).</summary>
    ApiKey,

    /// <summary>OAuth 2.0 client credentials grant.</summary>
    OAuth2
}
