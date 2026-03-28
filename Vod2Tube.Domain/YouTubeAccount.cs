namespace Vod2Tube.Domain
{
    public class YouTubeAccount
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The raw JSON content of the Google OAuth client_secrets.json file.
        /// </summary>
        public string ClientSecretsJson { get; set; } = string.Empty;

        public DateTime AddedAtUTC { get; set; }

        /// <summary>
        /// YouTube channel title discovered after successful authorization.
        /// </summary>
        public string ChannelTitle { get; set; } = string.Empty;
    }
}
