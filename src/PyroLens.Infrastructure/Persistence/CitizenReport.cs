using NetTopologySuite.Geometries;
using PyroLens.Core.Reports;

namespace PyroLens.Infrastructure.Persistence;

/// <summary>A row of <c>citizen_reports</c>.</summary>
public sealed class CitizenReport
{
    public long Id { get; set; }
    public required Point Location { get; set; }
    public ReportKind Kind { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public ReportStatus Status { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
