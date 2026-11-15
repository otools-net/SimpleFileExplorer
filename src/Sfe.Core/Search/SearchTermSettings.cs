using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>What the terms <c>type:</c>, <c>r:hr</c> and <c>r:lr</c> compare with: the current settings.</summary>
public sealed class SearchTermSettings(MediaClassifier classifier, IOptionsMonitor<SfeOptions> options)
{
    /// <summary>What <c>type:</c> compares with: the current file types of the listing.</summary>
    public MediaKinds MediaKinds() =>
        new(
            classifier.Extensions(ItemKind.Image),
            classifier.Extensions(ItemKind.Video),
            classifier.Extensions(ItemKind.Audio)
        );

    /// <summary>The thresholds of <c>r:hr</c> and <c>r:lr</c>: those of the HR/LR marks, current file types.</summary>
    public HighResolution HighResolution()
    {
        var browse = options.CurrentValue.Browse;
        return new HighResolution(
            classifier.Extensions(ItemKind.Image),
            Math.Clamp(browse.ImageHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels),
            classifier.Extensions(ItemKind.Video),
            Math.Clamp(browse.VideoHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels)
        );
    }
}
