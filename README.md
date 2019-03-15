# Token Vault Multi-Service Sample
Sample web app that uses Token Vault to manage access tokens to multiple external services. Users must sign in to the app using a work or school account. Then they can authorize the app to access their files from O365 and/or Dropbox.

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