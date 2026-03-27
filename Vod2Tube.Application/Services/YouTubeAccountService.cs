using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Microsoft.EntityFrameworkCore;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application.Services
{
    public class YouTubeAccountService
    {
        private readonly AppDbContext _dbContext;
        private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeUpload, YouTubeService.Scope.YoutubeReadonly };

        /// <summary>
        /// Root directory for per-account YouTube credential storage.
        /// Layout: {root}/accounts/{accountId}/token_store/
        /// </summary>
        private static readonly string AccountsRootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vod2Tube",
            "YouTubeCredentials",
            "accounts");

        // In-memory map used to correlate OAuth callbacks to account IDs.
        // The key is the OAuth state parameter, the value is the account ID.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> PendingAuthStates = new();

        public YouTubeAccountService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
            Directory.CreateDirectory(AccountsRootDir);
        }

        // ── CRUD ──────────────────────────────────────────────────────────────

        public async Task<List<YouTubeAccount>> GetAllAsync()
        {
            var accounts = await _dbContext.YouTubeAccounts.OrderBy(a => a.Name).ToListAsync();
            // Augment with token-file-based authorization status
            return accounts;
        }

        public async Task<YouTubeAccount?> GetByIdAsync(int id)
        {
            return await _dbContext.YouTubeAccounts.FindAsync(id);
        }

        public async Task<YouTubeAccount> CreateAsync(string name, string clientSecretsJson)
        {
            // Validate that the JSON is parseable as Google client secrets
            try
            {
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(clientSecretsJson));
                GoogleClientSecrets.FromStream(ms);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid client secrets JSON: {ex.Message}", ex);
            }

            var account = new YouTubeAccount
            {
                Name = name.Trim(),
                ClientSecretsJson = clientSecretsJson,
                AddedAtUTC = DateTime.UtcNow
            };

            _dbContext.YouTubeAccounts.Add(account);
            await _dbContext.SaveChangesAsync();
            return account;
        }

        public async Task<bool> UpdateAsync(int id, string name)
        {
            var existing = await _dbContext.YouTubeAccounts.FindAsync(id);
            if (existing == null) return false;

            existing.Name = name.Trim();
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var account = await _dbContext.YouTubeAccounts.FindAsync(id);
            if (account == null) return false;

            // Clear any channel references
            var channels = await _dbContext.Channels.Where(c => c.YouTubeAccountId == id).ToListAsync();
            foreach (var ch in channels)
                ch.YouTubeAccountId = null;

            _dbContext.YouTubeAccounts.Remove(account);
            await _dbContext.SaveChangesAsync();

            // Clean up token store on disk
            var accountDir = Path.Combine(AccountsRootDir, id.ToString());
            if (Directory.Exists(accountDir))
            {
                try { Directory.Delete(accountDir, true); } catch { /* best-effort */ }
            }

            return true;
        }

        // ── Authorization status ──────────────────────────────────────────────

        public bool IsAuthorized(int accountId)
        {
            var tokenStorePath = Path.Combine(AccountsRootDir, accountId.ToString(), "token_store");
            if (!Directory.Exists(tokenStorePath)) return false;

            // FileDataStore stores tokens as files; check if any exist
            return Directory.EnumerateFiles(tokenStorePath).Any();
        }

        // ── OAuth flow ────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a Google OAuth authorization URL for the given account.
        /// The user must visit this URL to grant access.
        /// </summary>
        public async Task<string?> GetAuthorizationUrlAsync(int accountId, string redirectUri)
        {
            var account = await _dbContext.YouTubeAccounts.FindAsync(accountId);
            if (account == null) return null;

            var clientSecrets = ParseClientSecrets(account.ClientSecretsJson);

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = clientSecrets.Secrets,
                Scopes = Scopes,
            });

            var state = Guid.NewGuid().ToString("N");
            PendingAuthStates[state] = accountId;

            var authUrl = flow.CreateAuthorizationCodeRequest(redirectUri);
            authUrl.State = state;

            return authUrl.Build().AbsoluteUri;
        }

        /// <summary>
        /// Handles the OAuth callback from Google. Exchanges the authorization code for tokens.
        /// </summary>
        public async Task<(bool Success, int AccountId, string? Error)> HandleOAuthCallbackAsync(
            string code, string state, string redirectUri)
        {
            if (!PendingAuthStates.TryRemove(state, out var accountId))
                return (false, 0, "Invalid or expired OAuth state parameter. Please try authorizing again.");

            var account = await _dbContext.YouTubeAccounts.FindAsync(accountId);
            if (account == null)
                return (false, accountId, "YouTube account not found.");

            try
            {
                var clientSecrets = ParseClientSecrets(account.ClientSecretsJson);

                var tokenStorePath = Path.Combine(AccountsRootDir, accountId.ToString(), "token_store");
                Directory.CreateDirectory(tokenStorePath);
                var tokenStore = new FileDataStore(tokenStorePath, fullPath: true);

                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = clientSecrets.Secrets,
                    Scopes = Scopes,
                    DataStore = tokenStore,
                });

                var tokenResponse = await flow.ExchangeCodeForTokenAsync(
                    "user", code, redirectUri, CancellationToken.None);

                // Try to fetch the YouTube channel title
                try
                {
                    var credential = new UserCredential(flow, "user", tokenResponse);
                    var ytService = new YouTubeService(new BaseClientService.Initializer
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "Vod2Tube"
                    });

                    var channelsRequest = ytService.Channels.List("snippet");
                    channelsRequest.Mine = true;
                    var channelsResponse = await channelsRequest.ExecuteAsync();

                    if (channelsResponse.Items?.Count > 0)
                    {
                        account.ChannelTitle = channelsResponse.Items[0].Snippet.Title;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch
                {
                    // Non-critical — the account is still authorized
                }

                return (true, accountId, null);
            }
            catch (Exception ex)
            {
                return (false, accountId, $"Failed to exchange authorization code: {ex.Message}");
            }
        }

        /// <summary>
        /// Revokes the stored tokens for an account so the user can re-authorize.
        /// </summary>
        public async Task<bool> RevokeAsync(int id)
        {
            var account = await _dbContext.YouTubeAccounts.FindAsync(id);
            if (account == null) return false;

            account.ChannelTitle = string.Empty;
            await _dbContext.SaveChangesAsync();

            var tokenStorePath = Path.Combine(AccountsRootDir, id.ToString(), "token_store");
            if (Directory.Exists(tokenStorePath))
            {
                try { Directory.Delete(tokenStorePath, true); } catch { /* best-effort */ }
            }
            return true;
        }

        // ── YouTube service for upload (used by VideoUploader) ────────────────

        /// <summary>
        /// Builds a <see cref="YouTubeService"/> authenticated for the given account ID.
        /// </summary>
        public async Task<YouTubeService> GetYouTubeServiceForAccountAsync(int accountId, CancellationToken ct)
        {
            var account = await _dbContext.YouTubeAccounts.FindAsync(accountId);
            if (account == null)
                throw new InvalidOperationException($"YouTube account #{accountId} not found.");

            var clientSecrets = ParseClientSecrets(account.ClientSecretsJson);

            var tokenStorePath = Path.Combine(AccountsRootDir, accountId.ToString(), "token_store");
            var tokenStore = new FileDataStore(tokenStorePath, fullPath: true);

            var existingToken = await tokenStore.GetAsync<TokenResponse>("user");
            if (existingToken == null)
                throw new InvalidOperationException(
                    $"YouTube account \"{account.Name}\" has not been authorized yet. " +
                    "Please authorize the account from the Accounts page.");

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = clientSecrets.Secrets,
                Scopes = Scopes,
                DataStore = tokenStore,
            });

            var credential = new UserCredential(flow, "user", existingToken);

            if (credential.Token.IsStale)
            {
                if (!await credential.RefreshTokenAsync(ct))
                {
                    await tokenStore.DeleteAsync<TokenResponse>("user");
                    throw new InvalidOperationException(
                        $"YouTube OAuth token for account \"{account.Name}\" has expired and could not be refreshed. " +
                        "Please re-authorize the account from the Accounts page.");
                }
            }

            return new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Vod2Tube"
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static GoogleClientSecrets ParseClientSecrets(string json)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            return GoogleClientSecrets.FromStream(ms);
        }
    }
}
