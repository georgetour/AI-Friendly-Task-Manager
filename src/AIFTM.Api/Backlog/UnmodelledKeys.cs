using YamlDotNet.RepresentationModel;

namespace AIFTM.Api.Backlog;

/// <summary>
/// The keys a mapping in BACKLOG.yaml carries that the index has no field for — a design doc link, a
/// blocker, anything a person or an agent wrote by hand. Opaque on purpose: the board never reads
/// them, so they are held exactly as parsed and handed back to <see cref="YamlIndex.Write"/>, which
/// is the only thing that looks inside. Whole-file writes would otherwise delete them on the first
/// click, and the file is somebody's only copy.
/// </summary>
public sealed class UnmodelledKeys
{
    public static readonly UnmodelledKeys None = new([]);

    internal UnmodelledKeys(IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> pairs) => Pairs = pairs;

    /// <summary>In the order the file had them, so writing them back is deterministic.</summary>
    internal IReadOnlyList<KeyValuePair<YamlNode, YamlNode>> Pairs { get; }
}
