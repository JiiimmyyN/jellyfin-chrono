namespace Chrono.RegistryTool.Requests;

public sealed class IssueForm
{
    private readonly Dictionary<string, string> _fields;

    private IssueForm(Dictionary<string, string> fields)
    {
        _fields = fields;
    }

    public static IssueForm Parse(string markdown)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("### ", StringComparison.Ordinal))
            {
                Store();
                current = raw[4..].Trim();
                lines.Clear();
            }
            else if (current is not null)
            {
                lines.Add(raw);
            }
        }

        Store();
        return new IssueForm(fields);

        void Store()
        {
            if (current is null)
            {
                return;
            }

            var value = StripFence(string.Join('\n', lines).Trim());
            fields[current] = value == "_No response_" ? string.Empty : value;
        }
    }

    private static string StripFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var lines = value.Split('\n').ToList();
        lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "```")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim();
    }

    public string Optional(string label) => _fields.TryGetValue(label, out var value) ? value : string.Empty;

    public string Required(string label)
    {
        var value = Optional(label);
        return string.IsNullOrWhiteSpace(value)
            ? throw new RequestException($"The field \"{label}\" is required.")
            : value;
    }

    public bool Checked(string label, string option)
        => Optional(label).Split('\n').Any(l => l.Trim().StartsWith("- [x]", StringComparison.OrdinalIgnoreCase) && l.Contains(option, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> List(string value)
        => value.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0)
            .ToList();
}

public sealed class RequestException(string message) : Exception(message);
