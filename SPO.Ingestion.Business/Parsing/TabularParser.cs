using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business;

public sealed class TabularParser
{
    private const long MaxXlsxExpandedBytes = 536_870_912;
    public IReadOnlyList<ParsedRow> Parse(Stream stream, string extension, SpoSourceDefinition source)
    {
        extension = extension.TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "csv" => ParseCsv(stream, source.MaxRows),
            "json" => ParseJson(stream, source.MaxRows),
            "xlsx" => ParseXlsx(stream, source.Sheet, source.MaxRows),
            _ => throw new InvalidOperationException($"Unsupported extension: {extension}.")
        };
    }

    private static IReadOnlyList<ParsedRow> ParseCsv(Stream stream, int maxRows)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = args => throw new InvalidDataException($"Invalid CSV near row {args.Context?.Parser?.Row}."),
            MissingFieldFound = null,
            TrimOptions = TrimOptions.None
        });
        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 })
            throw new InvalidDataException("CSV header is required.");
        var headers = csv.HeaderRecord!;
        EnsureUnique(headers);
        var rows = new List<ParsedRow>();
        while (csv.Read())
        {
            if (rows.Count == maxRows) throw new InvalidDataException($"Row limit {maxRows} exceeded.");
            rows.Add(new(rows.Count + 1, headers.ToDictionary(h => h, h => NullIfEmpty(csv.GetField(h)), StringComparer.OrdinalIgnoreCase)));
        }
        return rows;
    }

    private static IReadOnlyList<ParsedRow> ParseJson(Stream stream, int maxRows)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON root must be an array.");
        var rows = new List<ParsedRow>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Every JSON item must be an object.");
            if (rows.Count == maxRows) throw new InvalidDataException($"Row limit {maxRows} exceeded.");
            rows.Add(new(rows.Count + 1, item.EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.ValueKind is JsonValueKind.Null ? null : p.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
        }
        return rows;
    }

    private static IReadOnlyList<ParsedRow> ParseXlsx(Stream stream, string? sheetName, int maxRows)
    {
        EnsureSafeXlsx(stream);
        using var workbook = new XLWorkbook(stream);
        var sheet = string.IsNullOrWhiteSpace(sheetName) ? workbook.Worksheets.First() : workbook.Worksheet(sheetName);
        var range = sheet.RangeUsed() ?? throw new InvalidDataException("XLSX sheet is empty.");
        if (range.CellsUsed().Any(cell => cell.HasFormula))
            throw new InvalidDataException("XLSX formulas are not accepted; upload materialized values.");
        var headers = range.FirstRow().Cells().Select(c => c.GetString()).ToArray();
        if (headers.Length > 0) headers[0] = headers[0].TrimStart('\uFEFF');
        EnsureUnique(headers);
        var rows = new List<ParsedRow>();
        foreach (var row in range.RowsUsed().Skip(1))
        {
            if (rows.Count == maxRows) throw new InvalidDataException($"Row limit {maxRows} exceeded.");
            rows.Add(new(rows.Count + 1, headers.Select((h, i) => new { h, i }).ToDictionary(x => x.h,
                x => NullIfEmpty(row.Cell(x.i + 1).GetFormattedString(CultureInfo.InvariantCulture)), StringComparer.OrdinalIgnoreCase)));
        }
        return rows;
    }

    private static void EnsureSafeXlsx(Stream stream)
    {
        if (!stream.CanSeek) throw new InvalidDataException("XLSX input must be seekable.");
        var start = stream.Position;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                expanded = checked(expanded + entry.Length);
                if (expanded > MaxXlsxExpandedBytes)
                    throw new InvalidDataException($"XLSX expanded content exceeds {MaxXlsxExpandedBytes} bytes.");
            }
        }
        stream.Position = start;
    }

    private static void EnsureUnique(string[] headers)
    {
        if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("Headers must be non-empty and unique.");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
