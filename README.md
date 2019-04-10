# Token Vault Multi-Service Sample
Sample web app that uses Token Vault to manage access tokens to multiple external services. Users must sign in to the app using a work or school account. Then they can authorize the app to access their files from O365 and/or Dropbox.

![gif of sample being used](./assets/TokenVault.gif)

## Running the sample

### Register an AAD v2 app for app authentication
First you need to register an AAD v2 app, which will represent the web app's identity in the AAD world. The web app will use this AAD app to authenticate the user to the web app.

1. Go to the [AAD app registration page](https://ms.portal.azure.com/#blade/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/RegisteredAppsPreview) and click "New registration"
    - Name: choose any name
    - Supported accont types: Accounts in any organizational directory and personal Microsoft accounts

    Don't specify a redirect URI yet. You will fill this in later.
1. On the next page, note down the `Application ID`. You will need this later
1. In `Certificates & secrets`, create a new client secret and note it down. You will need this later

### Register an AAD v2 app for Graph authorization

Register another AAD v2 app by following the same steps as above. The web app will use this AAD app to get authorized access to the user's O365 files via the Graph APIs.

> Note: We need 2 separate AAD apps because the app authentication flow and Graph authorization flow will redirect to different domains, which is not allowed within a single AAD v2 app.

### Register a Dropbox app
Similarly you need to register a Dropbox app. The web app will use this Dropbox app to get authorized access to the user's Dropbox files.
1. Go to the [Dropbox developer site](https://www.dropbox.com/developers/apps) and click "Create app"
    - API: "Dropbox API"
    - Type of access: "Full Dropbox"
    - Name: choose any name
1. On the next page, note down the app key and app secret. You will need these later
1. Leave the "Redirect URIs" field blank. You will fill this in later
1. Set "Allow implicit grant" to "Disallow". Token Vault will use the authorization grant flow, so you can disable implicit flow to be safe

### Deploy the solution to Azure

[![Deploy to Azure](https://azuredeploy.net/deploybutton.png)](https://azuredeploy.net/)

This repository includes an ARM template that describes the necessary resources. You can easily deploy them to your Azure subscription using the button above. It will create 3 Azure resources:
- App Service: Hosts the sample web app
- App Service Plan: Defines the compute resources and pricing for the App Service
- Token Vault: Stores and manages OAuth access tokens

The template includes some paramters that you will have to fill in:

Parameter               | Description
----------------------- | ------------------------------------------------------------------
`tokenVaultName`        | Name of the Token Vault resource. Used in the Token Vault's URL
`webAppPlanName`        | Name for the App Service Plan resource
`webAppName`            | Name for the App Service resource. Used in the web app's URL
`dropboxAppId`          | App key assigned when you registered the Dropbox app
`dropboxAppSecret`      | App secret assigned when you registered the Dropbox app
`aadAuthNClientId`      | Client ID assigned when you registered the first AAD app
`aadAuthNClientSecret`  | Client secret created when you registered the first AAD app
`aadGraphClientId`      | Client ID assigned when you registered the second AAD app
`aadGraphClientSecret`  | Client secret created when you registered the second AAD app


### Set OAuth redirect URIs
One of the outputs of the deployment will be the Token Vault's redirect URI (`tokenVaultRedirectUri`). You will also have the URL to your deployed web app. You need to add these redirect URIs to your AAD and Dropbox app registrations.

1. Go back to the first AAD app (i.e. the one provided for `aadAuthNClientId`). Under `Authentication -> Redirect URIs`, add a redirect URI for your web app at `/signin-oidc` (for example, `https://mywebapp.azurewebsites.net/signin-oidc`)
1. Go back to the second AAD app (i.e. the one provided for `aadGraphClientId`). Under `Authentication -> Redirect URIs`, add the Token Vault redirect URI
1. Go back to the Dropbox app. Under `Redirect URIs`, add the Token Vault redirect URI


### Use the web app
Navigate to the App Service resource and click on the URL to open the application.

## Sample explanation

### Code structure

Here are the most relevant files and their roles in the sample:

- `Pages/Index.*`: The Razor Page for the main page of the app, where users log in to the app and connect to other services
- `Pages/Login.*`: The Razor Page that handles user log in to the app
- `Pages/PostAuth.*`: The Razor Page that handles the post-login redirect from Token Vault (after the auth flow for connecting to a service)
- `TokenVault/TokenVaultClient.cs`: A wrapper around the Token Vault runtime API
- `TokenVault/Token.cs`: Models used to deserialize the responses from the Token Vault API

### App authentication

Before the user can connect to various services, they must log in to the app itself, giving the app a user identity that it can later associate with the connected accounts. The sample implements authentication via AAD v2 using standard ASP.NET Core practices. It does not use Token Vault for this step. See [Startup.cs](./TokenVaultMultiService/Startup.cs) to see how this is implemented.

### Naming tokens

A token resource within Token Vault represents a single access token to a specific service. When creating a token resource, you must provide a name for it. The name must be unique (for a given service) and is used to refer to the token in other Token Vault API calls. It is also used in the login URLs that users click on, so it should **not** be a secret value such as the session ID.

The token name can be used to associate the token with a specific user of the app. The sample uses the object ID of the logged in user to name the tokens. The object ID comes from a token claim for the user:

```csharp
// Index.cshtml.cs -> OnGetAsync()

var objectId = this.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
```

Then the object ID is used to create and refer to the token in Token Vault:

```csharp
// Index.cshtml.cs -> OnGetAsync()

var tokenVaultDropboxToken = await GetOrCreateTokenResourceAsync(tokenVaultClient, "dropbox", objectId);
```

We use the same object ID to name all of that user's tokens, which is okay since the name only needs to be unique per service.

### Determining whether a service is connected

The token resource has a status field that tells whether the token is in a valid state. Before the user logs in, the token will be in an error state because it does not have an access token. Even if the user has logged in, it is possible for the token to re-enter an error state in the future. For example, if the user manually revokes authorization from your app, the next token refresh will fail.

The sample uses the token status to determine whether the user has connected to each service and show the appropriate UI. Specifically, it checks whether `Status.State` is "Ok":

```csharp
// Index.cshtml.cs -> OnGetAsync()

this.DropboxData.IsConnected = tokenVaultDropboxToken.Status.State.ToLower() == "ok";
```

If the state is "Error", you could check `Status.Error` for more details, but the sample does not do this. Anything other than "Ok" will result in the service showing as "Disconnected" with a link to log in.

### Protecting against phishing attacks

The [Token Vault GitHub repo](https://github.com/azure/azure-tokens) has a page describing a [phishing attack vulnerability](https://github.com/Azure/azure-tokens/blob/master/docs/phishing-attack-vulnerability.md) that you should protect against. The sample implements the mitigation described on that page.

Before starting the login flow, we save the token name (i.e. user's object ID) in the session state:

```csharp
// Index.cshtml.cs -> OnGetAsync()

this.HttpContext.Session.SetString("tvId", objectId);
```

> NOTE: The sample uses an in-memory session for simplicity, but this is not an appropriate implementation for production.

As part of the login URLs to connect to services, we include a post-login redirect URL to which Token Vault will redirect after the auth flow:

```csharp
// Index.cshtml.cs -> OnGetAsync()

var postAuthRedirectUrl = GetPostAuthRedirectUrl("dropbox", objectId);
this.DropboxData.LoginUrl = $"{tokenVaultDropboxToken.LoginUri}?PostLoginRedirectUrl={Uri.EscapeDataString(postAuthRedirectUrl)}";
```

Since we use the same redirect URL for every service and every user, the redirect handler will need to know the service name and token name, which we include as parameters when building the redirect URL:

```csharp
// Index.cshtml.cs -> GetPostAuthRedirectUrl()

var uriBuilder = new UriBuilder("https", this.Request.Host.Host, this.Request.Host.Port.GetValueOrDefault(-1), "postauth");
uriBuilder.Query = $"serviceId={serviceId}&tokenId={tokenId}";
```

After the user clicks on a login URL and authorizes access, the redirect handler checks whether the token name that we're handling matches the token name we saved in the session state earlier. If they are not equal, then the auth flow did not start from this session, so the handler quits immediately:

```csharp
// PostAuth.cshtml.cs -> OnGetAsync()

string expectedTokenId = this.HttpContext.Session.GetString("tvId");
string tokenId = this.HttpContext.Request.Query["tokenId"];
if (tokenId != expectedTokenId)
{
    throw new InvalidOperationException("token ID does not match expected value, will not save");
}
```

Otherwise, the verification passes, and the handler extracts the `code` given by Token Vault and calls "save" on the token resource. This commits the token in Token Vault and finalizes the auth flow:

```csharp
// PostAuth.cshtml.cs -> OnGetAsync()

string code = this.HttpContext.Request.Query["code"];
if (!String.IsNullOrWhiteSpace(code))
{
    ... // omitted creation of tokenVaultClient
    string serviceId = this.HttpContext.Request.Query["serviceId"];
    await tokenVaultClient.SaveTokenAsync(serviceId, tokenId, code);
}
```

Note that if the `serviceId` parameter is altered by an attacker, then the save operation will fail because the `code` is only valid for a specific service and token.

### Using the access token

If all goes well, the token resource will contain an access token that we can use to call other APIs:

```csharp
// Index.cshtml.cs -> OnGetAsync()

this.DropboxData.Files = await GetDropboxDocumentsAsync(tokenVaultDropboxToken.Value.AccessToken);
```