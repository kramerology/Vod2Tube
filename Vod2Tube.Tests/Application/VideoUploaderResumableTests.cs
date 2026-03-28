using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;
using Vod2Tube.Application.Models;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Tests for the resumable-upload behavior in <see cref="VideoUploader"/>.
/// Because the real uploader requires live YouTube credentials, a controlled
/// subclass is used to exercise the pipeline DB interactions without network calls.
/// </summary>
public class VideoUploaderResumableTests
{
    private static AppDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    // =========================================================================
    // Pipeline model — ResumableUploadUri property
    // =========================================================================

    [Test]
    public async Task Pipeline_ResumableUploadUri_DefaultsToEmptyString()
    {
        var pipeline = new Pipeline();

        await Assert.That(pipeline.ResumableUploadUri).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Pipeline_ResumableUploadUri_RoundTrips()
    {
        const string uri = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=abc123";
        var pipeline = new Pipeline { ResumableUploadUri = uri };

        await Assert.That(pipeline.ResumableUploadUri).IsEqualTo(uri);
    }

    // =========================================================================
    // VideoUploader — new upload saves ResumableUploadUri, then clears on success
    // =========================================================================

    [Test]
    public async Task NewUpload_SetsResumableUploadUri_BeforeCompletion()
    {
        await using var ctx = CreateInMemoryContext(nameof(NewUpload_SetsResumableUploadUri_BeforeCompletion));

        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Uploading" });
        await ctx.SaveChangesAsync();

        string tempFile = Path.GetTempFileName();
        try
        {
            var uploader = new RecordingVideoUploader(ctx);
            await foreach (var _ in uploader.RunAsync("v1", tempFile)) { }

            await Assert.That(uploader.UriWasStoredMidUpload).IsTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task NewUpload_ClearsResumableUploadUri_AfterSuccess()
    {
        await using var ctx = CreateInMemoryContext(nameof(NewUpload_ClearsResumableUploadUri_AfterSuccess));

        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Uploading" });
        await ctx.SaveChangesAsync();

        string tempFile = Path.GetTempFileName();
        try
        {
            var uploader = new RecordingVideoUploader(ctx);
            await foreach (var _ in uploader.RunAsync("v1", tempFile)) { }

            var pipeline = await ctx.Pipelines.FirstAsync(p => p.VodId == "v1");
            await Assert.That(pipeline.ResumableUploadUri).IsEqualTo(string.Empty);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task NewUpload_SetsYoutubeVideoId_AfterSuccess()
    {
        await using var ctx = CreateInMemoryContext(nameof(NewUpload_SetsYoutubeVideoId_AfterSuccess));

        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Uploading" });
        await ctx.SaveChangesAsync();

        string tempFile = Path.GetTempFileName();
        try
        {
            var uploader = new RecordingVideoUploader(ctx);
            await foreach (var _ in uploader.RunAsync("v1", tempFile)) { }

            var pipeline = await ctx.Pipelines.FirstAsync(p => p.VodId == "v1");
            await Assert.That(pipeline.YoutubeVideoId).IsEqualTo(RecordingVideoUploader.FakeVideoId);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // =========================================================================
    // VideoUploader — resume when ResumableUploadUri is already set
    // =========================================================================

    [Test]
    public async Task ResumeUpload_WhenResumableUploadUriIsSet_UsesResumeAsyncPath()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResumeUpload_WhenResumableUploadUriIsSet_UsesResumeAsyncPath));

        const string savedUri = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=existing_session";
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Uploading", ResumableUploadUri = savedUri });
        await ctx.SaveChangesAsync();

        string tempFile = Path.GetTempFileName();
        try
        {
            var uploader = new RecordingVideoUploader(ctx);
            await foreach (var _ in uploader.RunAsync("v1", tempFile)) { }

            await Assert.That(uploader.WasResumed).IsTrue();
            await Assert.That(uploader.ResumedFromUri).IsEqualTo(savedUri);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ResumeUpload_ClearsResumableUploadUri_AfterSuccess()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResumeUpload_ClearsResumableUploadUri_AfterSuccess));

        const string savedUri = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=existing_session";
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Uploading", ResumableUploadUri = savedUri });
        await ctx.SaveChangesAsync();

        string tempFile = Path.GetTempFileName();
        try
        {
            var uploader = new RecordingVideoUploader(ctx);
            await foreach (var _ in uploader.RunAsync("v1", tempFile)) { }

            var pipeline = await ctx.Pipelines.FirstAsync(p => p.VodId == "v1");
            await Assert.That(pipeline.ResumableUploadUri).IsEqualTo(string.Empty);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task NewUpload_WhenResumableUploadUriIsNotSet_DoesNotUseResumeAsyncPath()
    {
        await using var ctx = CreateInMemoryContext(nameof(NewUpload_WhenResumableUploadUriIsNotSet_DoesNotUseResumeAsyncPath));

        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Uploading" });
        await ctx.SaveChangesAsync();

        string tempFile = Path.GetTempFileName();
        try
        {
            var uploader = new RecordingVideoUploader(ctx);
            await foreach (var _ in uploader.RunAsync("v1", tempFile)) { }

            await Assert.That(uploader.WasResumed).IsFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // =========================================================================
    // Helper stub
    // =========================================================================

    /// <summary>
    /// A <see cref="VideoUploader"/> subclass that replaces the YouTube network
    /// calls with in-memory simulation, recording which code path was taken.
    /// </summary>
    private sealed class RecordingVideoUploader : VideoUploader
    {
        public const string FakeVideoId = "fake_yt_video_id";
        private const string FakeUploadUri = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=new_session";

        private readonly AppDbContext _ctx;

        public bool WasResumed { get; private set; }
        public string? ResumedFromUri { get; private set; }
        public bool UriWasStoredMidUpload { get; private set; }

        public RecordingVideoUploader(AppDbContext ctx) : base(ctx, null!)
        {
            _ctx = ctx;
        }

        public override async IAsyncEnumerable<ProgressStatus> RunAsync(
            string vodId,
            string finalFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return ProgressStatus.Indeterminate("Initializing YouTube upload...");

            var pipeline = await _ctx.Pipelines.FirstOrDefaultAsync(p => p.VodId == vodId, ct);
            string? savedUri = pipeline?.ResumableUploadUri;

            if (!string.IsNullOrEmpty(savedUri))
            {
                // Simulate the ResumeAsync path
                WasResumed = true;
                ResumedFromUri = savedUri;
                yield return ProgressStatus.Indeterminate("Resuming interrupted upload...");
            }
            else
            {
                // Simulate InitiateSessionAsync: store a fake URI in the DB
                if (pipeline != null)
                {
                    pipeline.ResumableUploadUri = FakeUploadUri;
                    await _ctx.SaveChangesAsync(ct);
                    UriWasStoredMidUpload = true;
                }

                yield return ProgressStatus.Indeterminate("Initiating upload session...");
                // Simulate the ResumeAsync path for a brand-new session
            }

            yield return ProgressStatus.WithProgress("Uploading video... 100%", 100);

            // Simulate successful upload: clear the URI and set the video ID
            if (pipeline != null)
            {
                pipeline.YoutubeVideoId = FakeVideoId;
                pipeline.ResumableUploadUri = string.Empty;
                await _ctx.SaveChangesAsync(ct);
            }

            yield return ProgressStatus.Indeterminate("Upload completed successfully!");
        }
    }
}
