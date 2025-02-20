﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DynamicData.Binding;
using NLog;
using StabilityMatrix.Avalonia.Extensions;
using StabilityMatrix.Avalonia.Models;
using StabilityMatrix.Avalonia.Models.Inference;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.Api.Comfy;
using StabilityMatrix.Core.Models.Api.Comfy.Nodes;
using StabilityMatrix.Core.Models.Api.Comfy.NodeTypes;
using StabilityMatrix.Core.Services;
using InferenceTextToImageView = StabilityMatrix.Avalonia.Views.Inference.InferenceTextToImageView;

#pragma warning disable CS0657 // Not a valid attribute location for this declaration

namespace StabilityMatrix.Avalonia.ViewModels.Inference;

[View(typeof(InferenceTextToImageView), persistent: true)]
public class InferenceTextToImageViewModel
    : InferenceGenerationViewModelBase,
        IParametersLoadableState
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly INotificationService notificationService;
    private readonly IModelIndexService modelIndexService;

    [JsonIgnore]
    public StackCardViewModel StackCardViewModel { get; }

    [JsonPropertyName("Model")]
    public ModelCardViewModel ModelCardViewModel { get; }

    [JsonPropertyName("Sampler")]
    public SamplerCardViewModel SamplerCardViewModel { get; }

    [JsonPropertyName("Prompt")]
    public PromptCardViewModel PromptCardViewModel { get; }

    [JsonPropertyName("Upscaler")]
    public UpscalerCardViewModel UpscalerCardViewModel { get; }

    [JsonPropertyName("HiresSampler")]
    public SamplerCardViewModel HiresSamplerCardViewModel { get; }

    [JsonPropertyName("HiresUpscaler")]
    public UpscalerCardViewModel HiresUpscalerCardViewModel { get; }

    [JsonPropertyName("FreeU")]
    public FreeUCardViewModel FreeUCardViewModel { get; }

    [JsonPropertyName("BatchSize")]
    public BatchSizeCardViewModel BatchSizeCardViewModel { get; }

    [JsonPropertyName("Seed")]
    public SeedCardViewModel SeedCardViewModel { get; }

    public bool IsFreeUEnabled
    {
        get => StackCardViewModel.GetCard<StackExpanderViewModel>().IsEnabled;
        set => StackCardViewModel.GetCard<StackExpanderViewModel>().IsEnabled = value;
    }

    public bool IsHiresFixEnabled
    {
        get => StackCardViewModel.GetCard<StackExpanderViewModel>(1).IsEnabled;
        set => StackCardViewModel.GetCard<StackExpanderViewModel>(1).IsEnabled = value;
    }

    public bool IsUpscaleEnabled
    {
        get => StackCardViewModel.GetCard<StackExpanderViewModel>(2).IsEnabled;
        set => StackCardViewModel.GetCard<StackExpanderViewModel>(2).IsEnabled = value;
    }

    public InferenceTextToImageViewModel(
        INotificationService notificationService,
        IInferenceClientManager inferenceClientManager,
        ServiceManager<ViewModelBase> vmFactory,
        IModelIndexService modelIndexService
    )
        : base(vmFactory, inferenceClientManager, notificationService)
    {
        this.notificationService = notificationService;
        this.modelIndexService = modelIndexService;

        // Get sub view models from service manager

        SeedCardViewModel = vmFactory.Get<SeedCardViewModel>();
        SeedCardViewModel.GenerateNewSeed();

        ModelCardViewModel = vmFactory.Get<ModelCardViewModel>();

        SamplerCardViewModel = vmFactory.Get<SamplerCardViewModel>(samplerCard =>
        {
            samplerCard.IsDimensionsEnabled = true;
            samplerCard.IsCfgScaleEnabled = true;
            samplerCard.IsSamplerSelectionEnabled = true;
            samplerCard.IsSchedulerSelectionEnabled = true;
        });

        PromptCardViewModel = vmFactory.Get<PromptCardViewModel>();
        HiresSamplerCardViewModel = vmFactory.Get<SamplerCardViewModel>(samplerCard =>
        {
            samplerCard.IsDenoiseStrengthEnabled = true;
        });
        HiresUpscalerCardViewModel = vmFactory.Get<UpscalerCardViewModel>();
        UpscalerCardViewModel = vmFactory.Get<UpscalerCardViewModel>();
        FreeUCardViewModel = vmFactory.Get<FreeUCardViewModel>();
        BatchSizeCardViewModel = vmFactory.Get<BatchSizeCardViewModel>();

        StackCardViewModel = vmFactory.Get<StackCardViewModel>();

        StackCardViewModel.AddCards(
            new LoadableViewModelBase[]
            {
                ModelCardViewModel,
                SamplerCardViewModel,
                // Free U
                vmFactory.Get<StackExpanderViewModel>(stackExpander =>
                {
                    stackExpander.Title = "FreeU";
                    stackExpander.AddCards(new LoadableViewModelBase[] { FreeUCardViewModel });
                }),
                // Hires Fix
                vmFactory.Get<StackExpanderViewModel>(stackExpander =>
                {
                    stackExpander.Title = "Hires Fix";
                    stackExpander.AddCards(
                        new LoadableViewModelBase[]
                        {
                            HiresUpscalerCardViewModel,
                            HiresSamplerCardViewModel
                        }
                    );
                }),
                vmFactory.Get<StackExpanderViewModel>(stackExpander =>
                {
                    stackExpander.Title = "Upscale";
                    stackExpander.AddCards(new LoadableViewModelBase[] { UpscalerCardViewModel });
                }),
                SeedCardViewModel,
                BatchSizeCardViewModel,
            }
        );

        // When refiner is provided in model card, enable for sampler
        ModelCardViewModel
            .WhenPropertyChanged(x => x.IsRefinerSelectionEnabled)
            .Subscribe(e =>
            {
                SamplerCardViewModel.IsRefinerStepsEnabled =
                    e.Sender is { IsRefinerSelectionEnabled: true, SelectedRefiner: not null };
            });
    }

    /// <inheritdoc />
    protected override void BuildPrompt(BuildPromptEventArgs args)
    {
        base.BuildPrompt(args);

        using var _ = CodeTimer.StartDebug();

        var builder = args.Builder;
        var nodes = builder.Nodes;

        if (args.SeedOverride is { } seed)
        {
            builder.Connections.Seed = Convert.ToUInt64(seed);
        }
        else
        {
            builder.Connections.Seed = Convert.ToUInt64(SeedCardViewModel.Seed);
        }

        // Setup empty latent
        builder.SetupLatentSource(BatchSizeCardViewModel, SamplerCardViewModel);

        // Setup base stage
        builder.SetupBaseSampler(
            SamplerCardViewModel,
            PromptCardViewModel,
            ModelCardViewModel,
            modelIndexService,
            postModelLoad: x =>
            {
                if (IsFreeUEnabled)
                {
                    builder.Connections.BaseModel = nodes
                        .AddNamedNode(
                            ComfyNodeBuilder.FreeU(
                                "FreeU",
                                x.Connections.BaseModel!,
                                FreeUCardViewModel.B1,
                                FreeUCardViewModel.B2,
                                FreeUCardViewModel.S1,
                                FreeUCardViewModel.S2
                            )
                        )
                        .Output;
                }
            }
        );

        // Setup refiner stage if enabled
        if (
            ModelCardViewModel is
            { IsRefinerSelectionEnabled: true, SelectedRefiner.IsDefault: false }
        )
        {
            builder.SetupRefinerSampler(
                SamplerCardViewModel,
                PromptCardViewModel,
                ModelCardViewModel,
                modelIndexService,
                postModelLoad: x =>
                {
                    if (IsFreeUEnabled)
                    {
                        builder.Connections.RefinerModel = nodes
                            .AddNamedNode(
                                ComfyNodeBuilder.FreeU(
                                    "Refiner_FreeU",
                                    x.Connections.RefinerModel!,
                                    FreeUCardViewModel.B1,
                                    FreeUCardViewModel.B2,
                                    FreeUCardViewModel.S1,
                                    FreeUCardViewModel.S2
                                )
                            )
                            .Output;
                    }
                }
            );
        }

        // Override with custom VAE if enabled
        if (ModelCardViewModel is { IsVaeSelectionEnabled: true, SelectedVae.IsDefault: false })
        {
            var customVaeLoader = nodes.AddNamedNode(
                ComfyNodeBuilder.VAELoader("VAELoader", ModelCardViewModel.SelectedVae.FileName)
            );

            builder.Connections.BaseVAE = customVaeLoader.Output;
            builder.Connections.RefinerVAE = customVaeLoader.Output;
        }

        // If hi-res fix is enabled, add the LatentUpscale node and another KSampler node
        if (args.Overrides.IsHiresFixEnabled ?? IsHiresFixEnabled)
        {
            // Requested upscale to this size
            var hiresSize = builder.Connections.GetScaledLatentSize(
                HiresUpscalerCardViewModel.Scale
            );

            LatentNodeConnection hiresLatent;

            // Select between latent upscale and normal upscale based on the upscale method
            var selectedUpscaler = HiresUpscalerCardViewModel.SelectedUpscaler!.Value;

            if (selectedUpscaler.Type == ComfyUpscalerType.None)
            {
                // If no upscaler selected or none, just use the latent image
                hiresLatent = builder.Connections.Latent!;
            }
            else
            {
                // Otherwise upscale the latent image
                hiresLatent = builder
                    .Group_UpscaleToLatent(
                        "HiresFix",
                        builder.Connections.Latent!,
                        builder.Connections.GetRefinerOrBaseVAE(),
                        selectedUpscaler,
                        hiresSize.Width,
                        hiresSize.Height
                    )
                    .Output;
            }

            // Use refiner model if set, or base if not
            var hiresSampler = nodes.AddNamedNode(
                ComfyNodeBuilder.KSampler(
                    "HiresSampler",
                    builder.Connections.GetRefinerOrBaseModel(),
                    builder.Connections.Seed,
                    HiresSamplerCardViewModel.Steps,
                    HiresSamplerCardViewModel.CfgScale,
                    // Use hires sampler name if not null, otherwise use the normal sampler
                    HiresSamplerCardViewModel.SelectedSampler
                        ?? SamplerCardViewModel.SelectedSampler
                        ?? throw new ValidationException("Sampler not selected"),
                    HiresSamplerCardViewModel.SelectedScheduler
                        ?? SamplerCardViewModel.SelectedScheduler
                        ?? throw new ValidationException("Scheduler not selected"),
                    builder.Connections.GetRefinerOrBaseConditioning(),
                    builder.Connections.GetRefinerOrBaseNegativeConditioning(),
                    hiresLatent,
                    HiresSamplerCardViewModel.DenoiseStrength
                )
            );

            // Set as latest latent
            builder.Connections.Latent = hiresSampler.Output;
            builder.Connections.LatentSize = hiresSize;
        }

        // If upscale is enabled, add another upscale group
        if (IsUpscaleEnabled)
        {
            var upscaleSize = builder.Connections.GetScaledLatentSize(UpscalerCardViewModel.Scale);

            // Build group
            var postUpscaleGroup = builder.Group_LatentUpscaleToImage(
                "PostUpscale",
                builder.Connections.Latent!,
                builder.Connections.GetRefinerOrBaseVAE(),
                UpscalerCardViewModel.SelectedUpscaler!.Value,
                upscaleSize.Width,
                upscaleSize.Height
            );

            // Set as the image output
            builder.Connections.Image = postUpscaleGroup.Output;
        }

        builder.SetupOutputImage();
    }

    /// <inheritdoc />
    protected override async Task GenerateImageImpl(
        GenerateOverrides overrides,
        CancellationToken cancellationToken
    )
    {
        // Validate the prompts
        if (!await PromptCardViewModel.ValidatePrompts())
        {
            return;
        }

        if (!await CheckClientConnectedWithPrompt() || !ClientManager.IsConnected)
        {
            return;
        }

        // If enabled, randomize the seed
        var seedCard = StackCardViewModel.GetCard<SeedCardViewModel>();
        if (overrides is not { UseCurrentSeed: true } && seedCard.IsRandomizeEnabled)
        {
            seedCard.GenerateNewSeed();
        }

        var batches = BatchSizeCardViewModel.BatchCount;

        var batchArgs = new List<ImageGenerationEventArgs>();

        for (var i = 0; i < batches; i++)
        {
            var seed = seedCard.Seed + i;

            var buildPromptArgs = new BuildPromptEventArgs
            {
                Overrides = overrides,
                SeedOverride = seed
            };
            BuildPrompt(buildPromptArgs);

            var generationArgs = new ImageGenerationEventArgs
            {
                Client = ClientManager.Client,
                Nodes = buildPromptArgs.Builder.ToNodeDictionary(),
                OutputNodeNames = buildPromptArgs.Builder.Connections.OutputNodeNames.ToArray(),
                Parameters = new GenerationParameters
                {
                    Seed = (ulong)seed,
                    Steps = SamplerCardViewModel.Steps,
                    CfgScale = SamplerCardViewModel.CfgScale,
                    Sampler = SamplerCardViewModel.SelectedSampler?.Name,
                    ModelName = ModelCardViewModel.SelectedModelName,
                    ModelHash = ModelCardViewModel
                        .SelectedModel
                        ?.Local
                        ?.ConnectedModelInfo
                        ?.Hashes
                        .SHA256,
                    PositivePrompt = PromptCardViewModel.PromptDocument.Text,
                    NegativePrompt = PromptCardViewModel.NegativePromptDocument.Text
                },
                Project = InferenceProjectDocument.FromLoadable(this),
                // Only clear output images on the first batch
                ClearOutputImages = i == 0
            };

            batchArgs.Add(generationArgs);
        }

        // Run batches
        foreach (var args in batchArgs)
        {
            await RunGeneration(args, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void LoadStateFromParameters(GenerationParameters parameters)
    {
        PromptCardViewModel.LoadStateFromParameters(parameters);
        SamplerCardViewModel.LoadStateFromParameters(parameters);

        SeedCardViewModel.Seed = Convert.ToInt64(parameters.Seed);

        ModelCardViewModel.LoadStateFromParameters(parameters);
    }

    /// <inheritdoc />
    public GenerationParameters SaveStateToParameters(GenerationParameters parameters)
    {
        parameters = PromptCardViewModel.SaveStateToParameters(parameters);
        parameters = SamplerCardViewModel.SaveStateToParameters(parameters);

        parameters.Seed = (ulong)SeedCardViewModel.Seed;

        parameters = ModelCardViewModel.SaveStateToParameters(parameters);

        return parameters;
    }
}
