namespace PaycomEntraProvisioner.Paycom;

public enum PaycomAuthMode
{
    /// <summary>SID + API token sent as the APISID / APIToken headers.</summary>
    ApiKey,

    /// <summary>OAuth 2.0 client credentials grant.</summary>
    OAuth2
}
