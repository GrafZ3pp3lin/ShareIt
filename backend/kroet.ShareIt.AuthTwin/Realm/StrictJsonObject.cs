using System.Text.Json;

namespace kroet.ShareIt.AuthTwin.Realm;

/// <summary>
/// Reads one JSON object of the realm file and fails on every key nobody read or ignored,
/// so a realm setting the twin doesn't model can't be skipped silently.
/// </summary>
internal sealed class StrictJsonObject
{
    private readonly JsonElement _element;
    private readonly HashSet<string> _seen = [];

    public StrictJsonObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Error(path, "must be an object");
        _element = element;
        Path = path;
    }

    public string Path { get; }

    public void Ignore(params string[] keys)
    {
        foreach (var key in keys) _seen.Add(key);
    }

    public string String(string key) =>
        OptionalString(key) ?? throw Error($"{Path}.{key}", "is required");

    public string? OptionalString(string key)
    {
        if (!TryGet(key, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw Error($"{Path}.{key}", "must be a string");
        return value.GetString();
    }

    public bool Bool(string key, bool defaultValue)
    {
        if (!TryGet(key, out var value)) return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Error($"{Path}.{key}", "must be true or false")
        };
    }

    public int Int(string key)
    {
        if (!TryGet(key, out var value)) throw Error($"{Path}.{key}", "is required");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw Error($"{Path}.{key}", "must be a whole number");
        return number;
    }

    /// <summary>Accepts the key only with this value; anything else is a setting the twin doesn't model.</summary>
    public void Expect(string key, bool expected, bool keycloakDefault)
    {
        if (Bool(key, keycloakDefault) != expected)
            throw Error($"{Path}.{key}", $"must be {(expected ? "true" : "false")}: the twin doesn't model anything else");
    }

    public void Expect(string key, string expected)
    {
        var actual = OptionalString(key);
        if (actual != expected)
            throw Error($"{Path}.{key}", $"must be \"{expected}\": the twin doesn't model anything else");
    }

    public IReadOnlyList<string> Strings(string key) =>
        [.. Elements(key).Select((e, i) => e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : throw Error($"{Path}.{key}[{i}]", "must be a string"))];

    public IReadOnlyList<StrictJsonObject> Objects(string key) =>
        [.. Elements(key).Select((e, i) => new StrictJsonObject(e, $"{Path}.{key}[{i}]"))];

    public StrictJsonObject? OptionalObject(string key) =>
        TryGet(key, out var value) ? new StrictJsonObject(value, $"{Path}.{key}") : null;

    public void EnsureAllRead()
    {
        var unknown = _element.EnumerateObject().Select(p => p.Name).Where(k => !_seen.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw Error(Path, $"has keys the twin doesn't know: {string.Join(", ", unknown)}");
    }

    public static RealmFileException Error(string path, string message) => new($"{path} {message}");

    private List<JsonElement> Elements(string key)
    {
        if (!TryGet(key, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw Error($"{Path}.{key}", "must be an array");
        return [.. value.EnumerateArray()];
    }

    private bool TryGet(string key, out JsonElement value)
    {
        _seen.Add(key);
        return _element.TryGetProperty(key, out value);
    }
}

internal sealed class RealmFileException(string message) : Exception(message);
