﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NLog;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Avalonia.Views.Dialogs;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper.Factory;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.Database;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Packages;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Avalonia.ViewModels.Dialogs;

[View(typeof(PackageImportDialog))]
public partial class PackageImportViewModel : ContentDialogViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IPackageFactory packageFactory;
    private readonly ISettingsManager settingsManager;

    [ObservableProperty]
    private DirectoryPath? packagePath;

    [ObservableProperty]
    private BasePackage? selectedBasePackage;

    public IReadOnlyList<BasePackage> AvailablePackages =>
        packageFactory.GetAllAvailablePackages().ToImmutableArray();

    [ObservableProperty]
    private PackageVersion? selectedVersion;

    [ObservableProperty]
    private ObservableCollection<GitCommit>? availableCommits;

    [ObservableProperty]
    private ObservableCollection<PackageVersion>? availableVersions;

    [ObservableProperty]
    private GitCommit? selectedCommit;

    // Version types (release or commit)
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ReleaseLabelText),
        nameof(IsReleaseMode),
        nameof(SelectedVersion)
    )]
    private PackageVersionType selectedVersionType = PackageVersionType.Commit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReleaseModeAvailable))]
    private PackageVersionType availableVersionTypes =
        PackageVersionType.GithubRelease | PackageVersionType.Commit;
    public string ReleaseLabelText => IsReleaseMode ? "Version" : "Branch";
    public bool IsReleaseMode
    {
        get => SelectedVersionType == PackageVersionType.GithubRelease;
        set =>
            SelectedVersionType = value
                ? PackageVersionType.GithubRelease
                : PackageVersionType.Commit;
    }

    public bool IsReleaseModeAvailable =>
        AvailableVersionTypes.HasFlag(PackageVersionType.GithubRelease);

    public PackageImportViewModel(IPackageFactory packageFactory, ISettingsManager settingsManager)
    {
        this.packageFactory = packageFactory;
        this.settingsManager = settingsManager;
    }

    public override async Task OnLoadedAsync()
    {
        SelectedBasePackage ??= AvailablePackages[0];

        if (Design.IsDesignMode)
            return;
        // Populate available versions
        try
        {
            var versionOptions = await SelectedBasePackage.GetAllVersionOptions();
            if (IsReleaseMode)
            {
                AvailableVersions = new ObservableCollection<PackageVersion>(
                    versionOptions.AvailableVersions
                );
                if (!AvailableVersions.Any())
                    return;

                SelectedVersion = AvailableVersions[0];
            }
            else
            {
                AvailableVersions = new ObservableCollection<PackageVersion>(
                    versionOptions.AvailableBranches
                );
                UpdateSelectedVersionToLatestMain();
            }
        }
        catch (Exception e)
        {
            Logger.Warn("Error getting versions: {Exception}", e.ToString());
        }
    }

    private static string GetDisplayVersion(string version, string? branch)
    {
        return branch == null ? version : $"{branch}@{version[..7]}";
    }

    // When available version types change, reset selected version type if not compatible
    partial void OnAvailableVersionTypesChanged(PackageVersionType value)
    {
        if (!value.HasFlag(SelectedVersionType))
        {
            SelectedVersionType = value;
        }
    }

    // When changing branch / release modes, refresh
    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSelectedVersionTypeChanged(PackageVersionType value) =>
        OnSelectedBasePackageChanged(SelectedBasePackage);

    partial void OnSelectedBasePackageChanged(BasePackage? value)
    {
        if (value is null || SelectedBasePackage is null)
        {
            AvailableVersions?.Clear();
            AvailableCommits?.Clear();
            return;
        }

        AvailableVersions?.Clear();
        AvailableCommits?.Clear();

        AvailableVersionTypes = SelectedBasePackage.AvailableVersionTypes;

        if (Design.IsDesignMode)
            return;

        Dispatcher.UIThread
            .InvokeAsync(async () =>
            {
                Logger.Debug($"Release mode: {IsReleaseMode}");
                var versionOptions = await value.GetAllVersionOptions();

                AvailableVersions = IsReleaseModeAvailable
                    ? new ObservableCollection<PackageVersion>(versionOptions.AvailableVersions)
                    : new ObservableCollection<PackageVersion>(versionOptions.AvailableBranches);

                Logger.Debug($"Available versions: {string.Join(", ", AvailableVersions)}");
                SelectedVersion = AvailableVersions[0];

                if (!IsReleaseMode)
                {
                    var commits = (await value.GetAllCommits(SelectedVersion.TagName))?.ToList();
                    if (commits is null || commits.Count == 0)
                        return;

                    AvailableCommits = new ObservableCollection<GitCommit>(commits);
                    SelectedCommit = AvailableCommits[0];
                    UpdateSelectedVersionToLatestMain();
                }
            })
            .SafeFireAndForget();
    }

    private void UpdateSelectedVersionToLatestMain()
    {
        if (AvailableVersions is null)
        {
            SelectedVersion = null;
        }
        else
        {
            // First try to find master
            var version = AvailableVersions.FirstOrDefault(x => x.TagName == "master");
            // If not found, try main
            version ??= AvailableVersions.FirstOrDefault(x => x.TagName == "main");

            // If still not found, just use the first one
            version ??= AvailableVersions[0];

            SelectedVersion = version;
        }
    }

    public async Task AddPackageWithCurrentInputs()
    {
        if (SelectedBasePackage is null || PackagePath is null)
            return;

        var version = new InstalledPackageVersion();
        if (IsReleaseMode)
        {
            version.InstalledReleaseVersion =
                SelectedVersion?.TagName
                ?? throw new NullReferenceException("Selected version is null");
        }
        else
        {
            version.InstalledBranch =
                SelectedVersion?.TagName
                ?? throw new NullReferenceException("Selected version is null");
            version.InstalledCommitSha =
                SelectedCommit?.Sha ?? throw new NullReferenceException("Selected commit is null");
        }

        var torchVersion = SelectedBasePackage.GetRecommendedTorchVersion();
        var sharedFolderRecommendation = SelectedBasePackage.RecommendedSharedFolderMethod;
        var package = new InstalledPackage
        {
            Id = Guid.NewGuid(),
            DisplayName = PackagePath.Name,
            PackageName = SelectedBasePackage.Name,
            LibraryPath = Path.Combine("Packages", PackagePath.Name),
            Version = version,
            LaunchCommand = SelectedBasePackage.LaunchCommand,
            LastUpdateCheck = DateTimeOffset.Now,
            PreferredTorchVersion = torchVersion,
            PreferredSharedFolderMethod = sharedFolderRecommendation
        };

        // Recreate venv if it's a BaseGitPackage
        if (SelectedBasePackage is BaseGitPackage gitPackage)
        {
            Logger.Info(
                "Recreating venv for imported package {Name} ({PackageName})",
                package.DisplayName,
                package.PackageName
            );
            await gitPackage.SetupVenv(
                PackagePath,
                forceRecreate: true,
                onConsoleOutput: output =>
                {
                    Logger.Debug("SetupVenv output: {Output}", output.Text);
                }
            );
        }

        // Reconfigure shared links
        Logger.Info(
            "Configuring shared links for imported package {Name} ({PackageName})",
            package.DisplayName,
            package.PackageName
        );
        var recommendedSharedFolderMethod = SelectedBasePackage.RecommendedSharedFolderMethod;
        await SelectedBasePackage.UpdateModelFolders(PackagePath, recommendedSharedFolderMethod);

        settingsManager.Transaction(s => s.InstalledPackages.Add(package));
    }
}
