using YamlDotNet.RepresentationModel;

namespace AIFTM.Tests;

/// <summary>
/// A backlog in the shape a real, hand-grown one arrives in: releases still on the stories (some of
/// them blank), each epic's version written after its title, and keys the index has never heard of
/// — a design doc link, a blocker — on the stories. Every key in a file like this is somebody's
/// data, so the tests built on it compare what the file says, key by key, before and after.
/// </summary>
internal static class OwnerShapedBacklog
{
    public const string Yaml = """
        project: Billing Remake
        roadmap:
        - V1
        - V2
        - V3
        epics:
        - number: 1
          title: Core Platform
          version: 0.1.0
          stories:
          - code: US-01
            title: Solution Skeleton
            status: Done
            release: V1
            folder: solution-skeleton
            doc: docs/architecture/US-01-skeleton.md
          - code: US-02
            title: Local Database
            status: In Progress
            release: ''
            folder: local-database
            blocked_by: US-01
        - number: 2
          title: CI/CD and Deployment Automation
          version: 0.2.0
          stories:
          - code: US-22
            title: Security Scanning (CodeQL)
            status: Not Yet Started
            release: V1
            folder: security-scanning-codeql
            doc: docs/maintenance-guides/ci-cd/US-22-security-scanning.md
            blocked_by: US-17
          - code: US-18
            title: CD Backend
            status: Not Yet Started
            release: ''
            folder: cd-backend
            blocked_by: US-21
        - number: 3
          title: Scaling
          version: 0.3.0
          stories:
          - code: US-30
            title: Caching
            status: Not Yet Started
            release: V3
            folder: caching
            doc: docs/scaling/US-30-caching.md
        """;

    /// <summary>The same backlog already in the current shape, written by hand rather than by the
    /// writer under test — a fixture produced by that writer would lose exactly what it should show.</summary>
    public const string ConvertedYaml = """
        project: Billing Remake
        roadmap:
        - V1
        - V2
        - V3
        epics:
        - number: 1
          version: 0.1.0
          release: V1
          title: Core Platform
          stories:
          - code: US-01
            title: Solution Skeleton
            status: Done
            folder: solution-skeleton
            doc: docs/architecture/US-01-skeleton.md
          - code: US-02
            title: Local Database
            status: In Progress
            folder: local-database
            blocked_by: US-01
        - number: 2
          version: 0.2.0
          release: V1
          title: CI/CD and Deployment Automation
          stories:
          - code: US-22
            title: Security Scanning (CodeQL)
            status: Not Yet Started
            folder: security-scanning-codeql
            doc: docs/maintenance-guides/ci-cd/US-22-security-scanning.md
            blocked_by: US-17
          - code: US-18
            title: CD Backend
            status: Not Yet Started
            folder: cd-backend
            blocked_by: US-21
        - number: 3
          version: 0.3.0
          release: V3
          title: Scaling
          stories:
          - code: US-30
            title: Caching
            status: Not Yet Started
            folder: caching
            doc: docs/scaling/US-30-caching.md

        """;

    private static readonly HashSet<string> StoryIndexKeys = ["code", "title", "status", "release", "folder"];

    /// <summary>Every story's keys outside the index, by story code, read with a generic YAML parser
    /// so a value that merely looks right in the text cannot pass.</summary>
    public static Dictionary<string, Dictionary<string, YamlNode>> StoryExtras(string yaml) =>
        Stories(yaml).ToDictionary(
            s => ((YamlScalarNode)s.Children[new YamlScalarNode("code")]).Value!,
            s => s.Children
                  .Where(kv => !StoryIndexKeys.Contains(((YamlScalarNode)kv.Key).Value!))
                  .ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => kv.Value));

    public static IEnumerable<YamlMappingNode> Stories(string yaml) =>
        Epics(yaml).SelectMany(e => e.Children.TryGetValue(new YamlScalarNode("stories"), out var s)
            ? ((YamlSequenceNode)s).Children.Cast<YamlMappingNode>()
            : []);

    public static IEnumerable<YamlMappingNode> Epics(string yaml) =>
        ((YamlSequenceNode)Root(yaml).Children[new YamlScalarNode("epics")]).Children.Cast<YamlMappingNode>();

    public static YamlMappingNode Root(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    public static string[] Keys(YamlMappingNode node) =>
        node.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToArray();

    public static void AssertStoryExtrasEqual(
        Dictionary<string, Dictionary<string, YamlNode>> expected,
        Dictionary<string, Dictionary<string, YamlNode>> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (code, extras) in expected)
        {
            Assert.Equal(extras.Keys, actual[code].Keys);
            foreach (var (key, value) in extras) Assert.Equal(value, actual[code][key]);
        }
    }
}
