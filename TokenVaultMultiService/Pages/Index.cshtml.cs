﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Dropbox.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;

namespace TokenVaultMultiService.Pages
{
    public class FileProviderViewData
    {
        public bool IsConnected { get; set; } = false;
        public IEnumerable<string> Files { get; set; } = Enumerable.Empty<string>();
        public string LoginUrl { get; set; }
    }

    public class IndexModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public FileProviderViewData DropboxData { get; set; }
        public FileProviderViewData GraphData { get; set; }
        public bool LoggedIn { get; set; }
        public string UserName { get; set; }

        public IndexModel(IConfiguration configuration)
        {
            this._configuration = configuration;
            this.DropboxData = new FileProviderViewData();
            this.GraphData = new FileProviderViewData();
        }

        public async Task OnGetAsync()
        {
            // Ensure that user is authenticated
            this.LoggedIn = this.User.Identity.IsAuthenticated;
            if (!this.LoggedIn)
            {
                return;
            }

            this.UserName = this.User.FindFirst("name").Value;
            var objectId = this.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;

            // Get an API token to access Token Vault
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var tokenVaultApiToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://tokenvault.azure.net");
            var tokenVaultUrl = this._configuration["TokenVaultUrl"];
            var tokenVaultClient = new TokenVault.TokenVaultClient(tokenVaultUrl, tokenVaultApiToken);

            // Get Token Vault token resource for Dropbox for this user (and create it if it doesn't exist)
            var tokenVaultDropboxToken = await GetOrCreateTokenResourceAsync(tokenVaultClient, "dropbox", objectId);

            // Check Dropbox token status and set in view data
            this.DropboxData.IsConnected = tokenVaultDropboxToken.Status.State.ToLower() == "ok";

            // If connected, get data from Dropbox and set in view data
            if (this.DropboxData.IsConnected)
            {
                this.DropboxData.Files = await GetDropboxDocumentsAsync(tokenVaultDropboxToken.Value.AccessToken);
            }
            // Otherwise, set Dropbox login URI in view data
            else
            {
                var postAuthRedirectUrl = GetPostAuthRedirectUrl("dropbox", objectId);
                this.DropboxData.LoginUrl = $"{tokenVaultDropboxToken.LoginUri}?PostLoginRedirectUrl={Uri.EscapeDataString(postAuthRedirectUrl)}";
            }



            // Get Token Vault token resource for Graph for this user (and create it if it doesn't exist)
            var tokenVaultGraphToken = await GetOrCreateTokenResourceAsync(tokenVaultClient, "graph", objectId);

            // Check Graph token status and set in view data
            this.GraphData.IsConnected = tokenVaultGraphToken.Status.State.ToLower() == "ok";

            // If connected, get data from Graph and set in view data
            if (this.GraphData.IsConnected)
            {
                this.GraphData.Files = await GetGraphDocumentsAsync(tokenVaultGraphToken.Value.AccessToken);
            }
            // Otherwise, set Graph login URI in view data
            else
            {
                var redirectUrl = GetPostAuthRedirectUrl("graph", objectId);
                this.GraphData.LoginUrl = $"{tokenVaultGraphToken.LoginUri}?PostLoginRedirectUrl={Uri.EscapeDataString(redirectUrl)}";
            }



            // Associate token name with this session, so that the post-auth handler can verify where the login flow originated
            this.HttpContext.Session.SetString("tvId", objectId);
        }

        #region Token Vault API methods

        private async Task<TokenVault.Token> GetOrCreateTokenResourceAsync(TokenVault.TokenVaultClient client, string serviceId, string tokenId)
        {
            var retrievedToken = await client.GetTokenResourceAsync(serviceId, tokenId);
            if (retrievedToken != null)
            {
                return retrievedToken;
            }

            return await client.CreateTokenResourceAsync(serviceId, tokenId);
        }

        #endregion

        #region Service APIs

        private async Task<IEnumerable<string>> GetDropboxDocumentsAsync(string token)
        {
            // Ensure token isn't empty
            if (string.IsNullOrEmpty(token))
            {
                return Enumerable.Empty<string>();
            }

            // Create DropboxClient and get file names
            using (var dbx = new DropboxClient(token))
            {
                var files = await dbx.Files.ListFolderAsync(string.Empty);
                var fileNames = files.Entries.Select(file => file.Name);
                return fileNames;
            }
        }

        private async Task<IEnumerable<string>> GetGraphDocumentsAsync(string token)
        {
            // Ensure token isn't empty
            if (string.IsNullOrEmpty(token))
            {
                return Enumerable.Empty<string>();
            }

            // Create GraphServiceClient and get file names
            var graphClient = new GraphServiceClient(new DelegateAuthenticationProvider((requestMessage) =>
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
                return Task.CompletedTask;
            }));
            var driveItems = await graphClient.Me.Drive.Root.Children.Request().GetAsync();
            var driveItemNames = driveItems.Select(driveItem => driveItem.Name);
            return driveItemNames;
        }

        #endregion

        #region Helper methods

        // Constructs the post-auth redirect URL that we append to Token Vault login URLs
        private string GetPostAuthRedirectUrl(string serviceId, string tokenId)
        {
            var uriBuilder = new UriBuilder("https", this.Request.Host.Host, this.Request.Host.Port.GetValueOrDefault(-1), "postauth");
            uriBuilder.Query = $"serviceId={serviceId}&tokenId={tokenId}";
            return uriBuilder.Uri.ToString();
        }

        #endregion
    }
}
