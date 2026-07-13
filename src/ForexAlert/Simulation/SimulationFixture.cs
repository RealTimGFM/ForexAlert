namespace ForexAlert.Simulation;

public sealed class SimulationFixture
{
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset EvaluateAtUtc { get; set; }

    public List<SimulationEvent> Events { get; set; } = [];
}

public sealed class SimulationEvent
{
    public string Type { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string? Side { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string? Interval { get; set; }

    public double? Price { get; set; }

    public double? Open { get; set; }

    public double? High { get; set; }

    public double? Low { get; set; }

    public double? Close { get; set; }
}
