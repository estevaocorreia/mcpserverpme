using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Meters;
using Mcpserver.Models;
using System.Globalization;

namespace Mcpserver.Application.Services;

public sealed class MeterService : IMeterService
{
    private readonly IMeterRepository _repo;

    public MeterService(IMeterRepository repo)
    {
        _repo = repo;
    }

    public Task<MeterListResult> ListAsync(MeterListRequest request, CancellationToken ct)
        => _repo.ListAsync(request, ct);



    public Task<MeterEnergyResult> Meter_EnergyAsync(MeterEnergyRequest request, CancellationToken ct)
    {
        var inicio = ParsePtBrDate(request.Inicio, nameof(request.Inicio));
        var fim = ParsePtBrDate(request.Fim, nameof(request.Fim));

        if (inicio >= fim)
            throw new ArgumentException("Inicio deve ser menor que Fim.");

        var query = new MeterEnergyQuery
        {
            Inicio = inicio,
            Fim = fim,
            DisplayNames = request.DisplayNames ?? new List<string>(),
            Take = request.Take
        };

        return _repo.Meter_EnergyAsync(query, ct);
    }

    public Task<MeterDemandResult> Meter_DemandAsync(MeterDemandRequest request, CancellationToken ct)
    {
        var inicio = ParsePtBrDate(request.Inicio, nameof(request.Inicio));
        var fim = ParsePtBrDate(request.Fim, nameof(request.Fim));

        if (inicio >= fim)
            throw new ArgumentException("Inicio deve ser menor que Fim.");

        var query = new MeterDemandQuery
        {
            Inicio = inicio,
            Fim = fim,
            DisplayNames = request.DisplayNames ?? new List<string>(),
            Take = request.Take
        };

        return _repo.Meter_DemandAsync(query, ct);
    }

    private static DateTime ParsePtBrDate(string? s, string field)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0)
            throw new ArgumentException($"{field} é obrigatório. Ex: \"25/02/2026\" ou \"25/02/2026 00:00\"");

        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var formats = new[]
        {
            "dd/MM/yyyy",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy HH:mm:ss"
        };

        if (DateTime.TryParseExact(s, formats, pt, DateTimeStyles.None, out var dt))
            return dt;

        if (DateTime.TryParse(s, pt, DateTimeStyles.None, out dt))
            return dt;

        throw new ArgumentException($"{field} inválido: \"{s}\". Use dd/MM/yyyy ou dd/MM/yyyy HH:mm");
    }



    public Task<MeterEventsByClassificationResult> Meter_EventsByClassificationAsync(
    MeterEventsByClassificationRequest request, CancellationToken ct)
    {
        var inicio = ParsePtBrDate(request.InicioUtc, nameof(request.InicioUtc));
        DateTime? fim = null;

        if (!string.IsNullOrWhiteSpace(request.FimUtc))
            fim = ParsePtBrDate(request.FimUtc, nameof(request.FimUtc));

        var query = new MeterEventsByClassificationQuery
        {
            InicioUtc = inicio,
            FimUtc = fim,
            DisplayNames = request.DisplayNames,
            Classifications = request.Classifications,
            Take = request.Take
        };

        return _repo.Meter_EventsByClassificationAsync(query, ct);
    }


}
