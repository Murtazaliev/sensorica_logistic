using System.Text.Json;
using DeliverySystem.Core.Intake;

namespace DeliverySystem.Infrastructure.Intake;

public sealed class AmoFixture
{
    public string EventType { get; set; } = "update_lead";
    public long AmoLeadId { get; set; }
    public long PipelineId { get; set; } = 1;
    public long StageId { get; set; }
    public string? StageName { get; set; }
    public long? ResponsibleAmoUserId { get; set; }
    public string? RequestedDate { get; set; }
    public long? PointExternalId { get; set; }
    public List<AmoFixtureItem> Items { get; set; } = [];
}

public sealed class AmoFixtureItem
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal Weight { get; set; }
}

public sealed class AmoFixtureRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _directory;

    public AmoFixtureRepository(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "amo");
    }

    public string Directory => _directory;

    public IReadOnlyList<string> ListScenarios()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(_directory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AmoFixture> LoadAsync(string scenario, CancellationToken ct = default)
    {
        var path = Path.Combine(_directory, scenario + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{scenario}' not found", path);
        }

        await using var stream = File.OpenRead(path);
        var fixture = await JsonSerializer.DeserializeAsync<AmoFixture>(stream, JsonOptions, ct);
        return fixture ?? throw new InvalidOperationException($"Fixture '{scenario}' is empty");
    }

    public async Task<LeadEvent> BuildEventAsync(
        string scenario,
        Guid tenantId,
        DateTimeOffset occurredAt,
        long? leadIdOverride,
        CancellationToken ct = default)
    {
        var path = Path.Combine(_directory, scenario + ".json");
        var fixture = await LoadAsync(scenario, ct);
        var raw = await File.ReadAllTextAsync(path, ct);

        DateOnly? requested = string.IsNullOrWhiteSpace(fixture.RequestedDate)
            ? null
            : DateOnly.Parse(fixture.RequestedDate);

        var items = fixture.Items
            .Select(i => new LeadItem(i.ProductName, i.Quantity, i.Unit, i.Weight))
            .ToArray();

        return new LeadEvent(
            tenantId,
            fixture.EventType,
            leadIdOverride ?? fixture.AmoLeadId,
            fixture.PipelineId,
            fixture.StageId,
            fixture.StageName,
            fixture.ResponsibleAmoUserId,
            fixture.PointExternalId,
            requested,
            items,
            raw,
            occurredAt);
    }
}
