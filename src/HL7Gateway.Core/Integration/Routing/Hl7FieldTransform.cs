using System.Text.Json;

namespace HL7Gateway.Core.Integration.Routing;

/// <summary>
/// 轻量 HL7 字段替换。TransformJson 示例：
/// [{"segment":"MSH","field":4,"value":"NEWFAC"}]
/// </summary>
public static class Hl7FieldTransform
{
    public static string Apply(string rawHl7, string? transformJson)
    {
        if (string.IsNullOrWhiteSpace(transformJson))
            return rawHl7;

        List<TransformSpec>? specs;
        try
        {
            specs = JsonSerializer.Deserialize<List<TransformSpec>>(transformJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return rawHl7;
        }

        if (specs is null || specs.Count == 0)
            return rawHl7;

        var lines = rawHl7.Replace("\n", "\r").Split('\r', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var pipe = line.IndexOf('|');
            if (pipe <= 0) continue;
            var seg = line[..pipe];

            foreach (var spec in specs)
            {
                if (!string.Equals(spec.Segment, seg, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (spec.Field < 0) continue;

                lines[i] = SetField(line, spec.Field, spec.Value ?? "");
            }
        }

        return string.Join("\r", lines);
    }

    private static string SetField(string segmentLine, int fieldIndex, string value)
    {
        var parts = segmentLine.Split('|');
        while (parts.Length <= fieldIndex)
            Array.Resize(ref parts, parts.Length + 1);

        parts[fieldIndex] = value;
        return string.Join('|', parts);
    }

    private sealed class TransformSpec
    {
        public string Segment { get; set; } = "";
        public int Field { get; set; }
        public string? Value { get; set; }
    }
}
