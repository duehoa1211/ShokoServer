using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Shoko.Commons.Utils;
using Shoko.Models.Enums;
using Shoko.Server.Extensions;
using Shoko.Server.ImageDownload;
using Shoko.Server.Providers;
using Shoko.Server.Providers.AniDB;
using Shoko.Server.Providers.AniDB.Interfaces;
using Shoko.Server.Repositories;
using Shoko.Server.Scheduling.Acquisition.Attributes;
using Shoko.Server.Scheduling.Attributes;
using Shoko.Server.Scheduling.Concurrency;

namespace Shoko.Server.Scheduling.Jobs.AniDB;

[DatabaseRequired]
[NetworkRequired]
[LimitConcurrency(8, 16)]
[JobKeyGroup(JobKeyGroup.AniDB)]
public class DownloadAniDBImageJob : BaseJob, IImageDownloadJob
{
    private const string FailedToDownloadNoImpl = "Image failed to download: No implementation found for {ImageType}";
    private readonly AniDBImageHandler _imageHandler;
    public string Anime { get; set; }
    public int ImageID { get; set; }
    public bool ForceDownload { get; set; }

    public ImageEntityType ImageType { get; set; }

    public override string TypeName => "Download AniDB Image";

    public override string Title => "Downloading AniDB Image";
    public override Dictionary<string, object> Details
    {
        get
        {
            return ImageType switch
            {
                ImageEntityType.AniDB_Cover when Anime != null => new()
                {
                    {
                        "Type", ImageType.ToString().Replace("_", " ")
                    },
                    {
                        "Anime", Anime
                    },
                },
                ImageEntityType.AniDB_Cover when Anime == null => new()
                {
                    {
                        "Type", ImageType.ToString().Replace("_", " ")
                    },
                    {
                        "AnimeID", ImageID
                    },
                },
                _ => new()
                {
                    {
                        "Anime", Anime
                    },
                    {
                        "Type", ImageType.ToString().Replace("_", " ")
                    },
                    {
                        "ImageID", ImageID
                    }
                }
            };
        }
    }

    public override async Task Process()
    {
        _logger.LogInformation("Processing {Job} for {Anime} -> Cover: {ImageType} | ImageID: {EntityID}", nameof(DownloadAniDBImageJob), Anime, ImageType, ImageID);

        var (downloadUrl, filePath) = _imageHandler.GetPaths(ImageType, ImageID);

        if (downloadUrl == null || filePath == null)
        {
            _logger.LogWarning(FailedToDownloadNoImpl, ImageType.ToString());
            return;
        }

        try
        {
            // If this has any issues, it will throw an exception, so the catch below will handle it.
            var result = await DownloadNow(downloadUrl, filePath);
            switch (result)
            {
                case ImageDownloadResult.Success:
                    _logger.LogInformation("Image downloaded for {Anime}: {FilePath} from {DownloadUrl}", Anime, filePath, downloadUrl);
                    break;
                case ImageDownloadResult.Cached:
                    _logger.LogDebug("Image already in cache for {Anime}: {FilePath} from {DownloadUrl}", Anime, filePath, downloadUrl);
                    break;
                case ImageDownloadResult.Failure:
                    _logger.LogWarning("Image failed to download for {Anime}: {FilePath} from {DownloadUrl}", Anime, filePath, downloadUrl);
                    break;
                case ImageDownloadResult.RemovedResource:
                    _logger.LogWarning("Image failed to download for {Anime} and the local entry has been removed: {FilePath} from {DownloadUrl}", Anime,
                        filePath, downloadUrl);
                    break;
                case ImageDownloadResult.InvalidResource:
                    _logger.LogWarning("Image failed to download for {Anime} and the local entry could not be removed: {FilePath} from {DownloadUrl}",
                        Anime, filePath, downloadUrl);
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning("Error processing {Job} for {Anime}: {Url} ({EntityID}) - {Message}", nameof(DownloadAniDBImageJob), Anime, downloadUrl,
                ImageID, e.Message);
        }
    }

    private Task<ImageDownloadResult> DownloadNow(string downloadUrl, string filePath, int maxRetries = 5)
    {
        return _imageHandler.DownloadImage(downloadUrl, filePath, ForceDownload, maxRetries);
    }

    public DownloadAniDBImageJob(AniDBImageHandler imageHandler)
    {
        _imageHandler = imageHandler;
    }

    protected DownloadAniDBImageJob() { }
}
