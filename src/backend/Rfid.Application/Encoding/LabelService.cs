using System.Text.RegularExpressions;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Builds ZPL for RFID label printers (Zebra ZT411R/ZD621R etc.): encodes the EPC into the tag and prints identifier, name and a barcode.</summary>
public static class LabelService
{
    /// <summary>Default 4x2" label: EPC written to the tag's EPC bank, Code-128 barcode of the identifier, human-readable text.</summary>
    public const string DefaultTemplate = "^XA\n^CI28\n^PW812\n^LL406\n^RS8\n^RFW,H,,,A^FD{epc}^FS\n^FO30,30^A0N,40,40^FD{name}^FS\n^FO30,85^A0N,28,28^FD{type} · {identifier}^FS\n^FO30,130^BY3^BCN,120,Y,N,N^FD{identifier}^FS\n^FO30,300^A0N,24,24^FD{location}^FS\n^FO30,340^A0N,20,20^FDEPC {epc}^FS\n^XZ";

    public static string Render(Item item, string epc, string? template = null)
    {
        var tpl = string.IsNullOrWhiteSpace(template) ? item.ItemType?.LabelTemplate is { Length: > 0 } t ? t : DefaultTemplate : template;
        return Regex.Replace(tpl, @"\{([a-zA-Z0-9_.]+)\}", m =>
        {
            var key = m.Groups[1].Value;
            if (key.StartsWith("attributes.", StringComparison.OrdinalIgnoreCase)) return Esc(item.Attributes.GetValueOrDefault(key[11..])?.ToString());
            return key.ToLowerInvariant() switch
            {
                "epc" => epc, "name" => Esc(item.Name), "identifier" => Esc(item.Identifier), "type" => Esc(item.ItemType?.Name), "typecode" => Esc(item.ItemType?.Code),
                "location" => Esc(item.CurrentLocation?.Name), "state" => Esc(item.State), "lot" => Esc(item.LotNumber), "expiry" => item.ExpiryDate?.ToString("yyyy-MM-dd") ?? "",
                "quantity" => item.Quantity.ToString("0.##"), "date" => DateTime.UtcNow.ToString("yyyy-MM-dd"), _ => m.Value,
            };
        });
    }

    /// <summary>ZPL control characters (^ ~) inside field data would break the label.</summary>
    private static string Esc(string? s) => (s ?? "").Replace("^", " ").Replace("~", " ");
}
