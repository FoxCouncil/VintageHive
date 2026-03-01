// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace UsenetCurator;

internal class PipelineConfig
{
    public List<GroupDefinition> Groups { get; init; }

    public Dictionary<string, int?> MaxPerGroup { get; init; }

    public Dictionary<string, int> ReadLimits { get; init; }

    public int MinYear { get; init; }

    public int MaxYear { get; init; }

    public string OutputDir { get; init; }

    public bool IsCiMode { get; init; }
}
