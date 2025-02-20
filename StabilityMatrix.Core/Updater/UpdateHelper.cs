﻿using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models.Configs;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Progress;
using StabilityMatrix.Core.Models.Update;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Core.Updater;

public class UpdateHelper : IUpdateHelper
{
    private readonly ILogger<UpdateHelper> logger;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IDownloadService downloadService;
    private readonly DebugOptions debugOptions;
    private readonly System.Timers.Timer timer = new(TimeSpan.FromMinutes(60));

    private string UpdateManifestUrl =>
        debugOptions.UpdateManifestUrl ?? "https://cdn.lykos.ai/update-v2.json";

    public const string UpdateFolderName = ".StabilityMatrixUpdate";
    public static DirectoryPath UpdateFolder => Compat.AppCurrentDir.JoinDir(UpdateFolderName);

    public static FilePath ExecutablePath => UpdateFolder.JoinFile(Compat.GetExecutableName());

    public UpdateHelper(
        ILogger<UpdateHelper> logger,
        IHttpClientFactory httpClientFactory,
        IDownloadService downloadService,
        IOptions<DebugOptions> debugOptions
    )
    {
        this.logger = logger;
        this.httpClientFactory = httpClientFactory;
        this.downloadService = downloadService;
        this.debugOptions = debugOptions.Value;

        timer.Elapsed += async (_, _) =>
        {
            await CheckForUpdate().ConfigureAwait(false);
        };
    }

    public async Task StartCheckingForUpdates()
    {
        timer.Enabled = true;
        timer.Start();
        await CheckForUpdate().ConfigureAwait(false);
    }

    public async Task DownloadUpdate(UpdateInfo updateInfo, IProgress<ProgressReport> progress)
    {
        var downloadUrl = updateInfo.DownloadUrl;

        UpdateFolder.Create();
        UpdateFolder.Info.Attributes |= FileAttributes.Hidden;

        // download the file from URL
        await downloadService
            .DownloadToFileAsync(
                downloadUrl,
                ExecutablePath,
                progress: progress,
                httpClientName: "UpdateClient"
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Format a DatetimeOffset to a culture invariant string for use in signature verification.
    /// </summary>
    private static string FormatDateTimeOffsetInvariant(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToString(
            @"yyyy-MM-ddTHH\:mm\:ss.ffffffzzz",
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>
    /// Data for use in signature verification.
    /// Semicolon separated string of fields:
    /// "version, releaseDate, channel, type, url, changelog, hashBlake3"
    /// </summary>
    private static string GetUpdateInfoSignedData(UpdateInfo updateInfo)
    {
        var channel = updateInfo.Channel.GetStringValue().ToLowerInvariant();
        var date = FormatDateTimeOffsetInvariant(updateInfo.ReleaseDate);
        return $"{updateInfo.Version};{date};{channel};"
            + $"{(int)updateInfo.Type};{updateInfo.DownloadUrl};{updateInfo.ChangelogUrl};"
            + $"{updateInfo.HashBlake3}";
    }

    private async Task CheckForUpdate()
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient("UpdateClient");
            var response = await httpClient.GetAsync(UpdateManifestUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Error while checking for update {StatusCode} - {Content}",
                    response.StatusCode,
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                );
                return;
            }

            var updateCollection = await JsonSerializer
                .DeserializeAsync<UpdateCollection>(
                    await response.Content.ReadAsStreamAsync().ConfigureAwait(false)
                )
                .ConfigureAwait(false);

            if (updateCollection is null)
            {
                logger.LogError("UpdateCollection is null");
                return;
            }

            // Get the update info for our platform
            var updateInfo = updateCollection switch
            {
                _ when Compat.IsWindows && Compat.IsX64 => updateCollection.WindowsX64,
                _ when Compat.IsLinux && Compat.IsX64 => updateCollection.LinuxX64,
                _ => null
            };

            if (updateInfo is null)
            {
                logger.LogWarning(
                    "Could not find compatible update info for the platform {Platform}",
                    Compat.Platform
                );
                return;
            }

            logger.LogInformation("UpdateInfo signature: {Signature}", updateInfo.Signature);

            var updateInfoSignData = GetUpdateInfoSignedData(updateInfo);
            logger.LogInformation("UpdateInfo signed data: {SignData}", updateInfoSignData);

            // Verify signature
            var checker = new SignatureChecker();
            if (!checker.Verify(updateInfoSignData, updateInfo.Signature))
            {
                logger.LogError("UpdateInfo signature is invalid: {Info}", updateInfo);
                return;
            }
            logger.LogInformation("UpdateInfo signature verified");

            var order = updateInfo.Version.ComparePrecedenceTo(Compat.AppVersion);

            if (order > 0)
            {
                // Newer version available
                logger.LogInformation(
                    "Update available {AppVer} -> {UpdateVer}",
                    Compat.AppVersion,
                    updateInfo.Version
                );
                EventManager.Instance.OnUpdateAvailable(updateInfo);
                return;
            }
            if (order == 0)
            {
                // Same version available, check if we both have commit hash metadata
                var updateHash = updateInfo.Version.Metadata;
                var appHash = Compat.AppVersion.Metadata;
                // If different, we can update
                if (updateHash != appHash)
                {
                    logger.LogInformation(
                        "Update available {AppVer} -> {UpdateVer}",
                        Compat.AppVersion,
                        updateInfo.Version
                    );
                    EventManager.Instance.OnUpdateAvailable(updateInfo);
                    return;
                }
            }

            logger.LogInformation("No update available");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Couldn't check for update");
        }
    }
}
