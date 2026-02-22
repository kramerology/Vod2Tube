# YouTube API Setup Instructions

To enable YouTube video uploads, you need to set up OAuth 2.0 credentials from Google Cloud Console.

## Setup Steps

1. **Go to Google Cloud Console**: https://console.cloud.google.com/

2. **Create a new project** (or select an existing one)

3. **Enable YouTube Data API v3**:
   - Go to "APIs & Services" > "Library"
   - Search for "YouTube Data API v3"
   - Click "Enable"

4. **Create OAuth 2.0 Credentials**:
   - Go to "APIs & Services" > "Credentials"
   - Click "Create Credentials" > "OAuth client ID"
   - If prompted, configure the OAuth consent screen first
   - Choose "Desktop app" as the application type
   - Give it a name (e.g., "Vod2Tube Desktop Client")
   - Click "Create"

5. **Download the credentials**:
   - Click the download button (⬇️) next to your newly created OAuth 2.0 Client ID
   - Save the downloaded JSON file as `client_secrets.json` in this directory (`YouTubeCredentials/`)

## File Structure

After setup, this directory should contain:
```
YouTubeCredentials/
├── client_secrets.json         (Your OAuth 2.0 credentials - DO NOT COMMIT!)
├── token_store/                (Auto-generated, stores auth tokens)
└── README.md                   (This file)
```

## First Run

On the first run, the application will:
1. Open a browser window for you to authenticate with your Google account
2. Ask for permission to upload videos to YouTube
3. Save the authentication token locally for future use

## Security Notes

⚠️ **IMPORTANT**: 
- Never commit `client_secrets.json` to version control
- Never share your OAuth credentials
- The `token_store/` directory contains sensitive authentication tokens
- Consider adding these to your `.gitignore`:
  ```
  YouTubeCredentials/client_secrets.json
  YouTubeCredentials/token_store/
  ```

## VideoUploaderOptions

The uploader supports the following options (configured in code):

- **Title**: Video title (auto-populated from VOD metadata)
- **Description**: Video description (includes streamer info and VOD URL)
- **Category**: YouTube category ID (default: "20" for Gaming)
- **PrivacyStatus**: "private", "public", or "unlisted" (default: "private")
- **Tags**: List of tags for the video
- **PlaylistId**: Optional playlist ID to add the video to

## Common YouTube Category IDs

- 20 - Gaming
- 22 - People & Blogs
- 23 - Comedy
- 24 - Entertainment
- 25 - News & Politics

For a complete list, see: https://developers.google.com/youtube/v3/docs/videoCategories/list
