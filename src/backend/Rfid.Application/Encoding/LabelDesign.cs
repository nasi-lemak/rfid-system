using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rfid.Application.Labels;

/// <summary>
/// Declarative label document edited by the web designer and compiled to ZPL. Units are millimetres;
/// ZPL dots are derived from the printer density (dpmm: 8 = 203 dpi, 12 = 300 dpi).
/// </summary>
public class LabelDesign
{
    public double WidthMm { get; set; } = 101.6;
    public double HeightMm { get; set; } = 50.8;
    public int Dpmm { get; set; } = 8;
    public bool EncodeRfid { get; set; } = true;
    public List<LabelElement> Elements { get; set; } = new();

    public static LabelDesign Default() => new()
    {
        Elements =
        {
            new() { Type = "text", X = 4, Y = 4, FontSizeMm = 5, Bold = true, Text = "{name}" },
            new() { Type = "text", X = 4, Y = 11, FontSizeMm = 3.5, Text = "{type} · {identifier}" },
            new() { Type = "barcode", X = 4, Y = 17, Height = 14, Text = "{identifier}", ModuleWidth = 3 },
            new() { Type = "text", X = 4, Y = 38, FontSizeMm = 3, Text = "{location}" },
            new() { Type = "text", X = 4, Y = 43, FontSizeMm = 2.5, Text = "EPC {epc}" },
            new() { Type = "qr", X = 80, Y = 28, Text = "{identifier}", Magnification = 4 },
        },
    };

    public static LabelDesign? Parse(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LabelDesign>(json, JsonOpts);
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

/// <summary>type: text | barcode (Code 128) | qr | box | line. Coordinates/sizes in mm. Text may contain {placeholders}.</summary>
public class LabelElement
{
    public string Type { get; set; } = "text";
    public double X { get; set; }
    public double Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Text { get; set; }
    public double FontSizeMm { get; set; } = 3;
    public bool Bold { get; set; }
    public int Rotation { get; set; } // 0, 90, 180, 270
    public int ModuleWidth { get; set; } = 2; // barcode module width in dots
    public int Magnification { get; set; } = 4; // QR
    public double Thickness { get; set; } = 0.5; // box/line border mm
}

public static class LabelCompiler
{
    /// <summary>Compiles a design into a ZPL *template* (placeholders kept) that LabelService.Render fills per item.</summary>
    public static string Compile(LabelDesign d)
    {
        int Dots(double mm) => (int)Math.Round(mm * d.Dpmm);
        var sb = new StringBuilder();
        sb.Append("^XA\n^CI28\n").Append("^PW").Append(Dots(d.WidthMm)).Append("\n^LL").Append(Dots(d.HeightMm)).Append("\n^LH0,0\n");
        if (d.EncodeRfid) sb.Append("^RS8\n^RFW,H,,,A^FD{epc}^FS\n");
        foreach (var e in d.Elements)
        {
            var rot = e.Rotation switch { 90 => "R", 180 => "I", 270 => "B", _ => "N" };
            switch (e.Type.ToLowerInvariant())
            {
                case "text":
                {
                    var h = Dots(e.FontSizeMm); var w = e.Bold ? (int)Math.Round(h * 1.1) : h;
                    sb.Append("^FO").Append(Dots(e.X)).Append(',').Append(Dots(e.Y));
                    if (e.Width.HasValue) sb.Append("^FB").Append(Dots(e.Width.Value)).Append(",1,0,L");
                    sb.Append("^A0").Append(rot).Append(',').Append(h).Append(',').Append(w).Append("^FD").Append(Esc(e.Text)).Append("^FS\n");
                    break;
                }
                case "barcode":
                    sb.Append("^FO").Append(Dots(e.X)).Append(',').Append(Dots(e.Y)).Append("^BY").Append(Math.Clamp(e.ModuleWidth, 1, 10)).Append("^BC").Append(rot).Append(',').Append(Dots(e.Height ?? 12)).Append(",Y,N,N^FD").Append(Esc(e.Text)).Append("^FS\n");
                    break;
                case "qr":
                    sb.Append("^FO").Append(Dots(e.X)).Append(',').Append(Dots(e.Y)).Append("^BQ").Append(rot).Append(",2,").Append(Math.Clamp(e.Magnification, 1, 10)).Append("^FDQA,").Append(Esc(e.Text)).Append("^FS\n");
                    break;
                case "box":
                    sb.Append("^FO").Append(Dots(e.X)).Append(',').Append(Dots(e.Y)).Append("^GB").Append(Dots(e.Width ?? 10)).Append(',').Append(Dots(e.Height ?? 10)).Append(',').Append(Math.Max(1, Dots(e.Thickness))).Append("^FS\n");
                    break;
                case "line":
                    sb.Append("^FO").Append(Dots(e.X)).Append(',').Append(Dots(e.Y)).Append("^GB").Append(Dots(e.Width ?? 10)).Append(',').Append(Math.Max(1, Dots(e.Height ?? e.Thickness))).Append(',').Append(Math.Max(1, Dots(e.Thickness))).Append("^FS\n");
                    break;
            }
        }
        sb.Append("^XZ");
        return sb.ToString();
    }

    private static string Esc(string? s) => (s ?? "").Replace("^", " ").Replace("~", " ");
    public static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
