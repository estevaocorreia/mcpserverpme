namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterDetailsDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";

    public long? NamespaceId { get; set; }
    public int? SourceTypeId { get; set; }
    public string? SourceTypeName { get; set; }

    public int? TimeZoneId { get; set; }
    public string? Description { get; set; }
    public string? Signature { get; set; }
    public string? DisplayName { get; set; }
}
