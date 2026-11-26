#nullable enable

using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace ClassicUO.Configuration;

/// <summary>Combines independent JSON edits while preserving both answers to conflicting edits.</summary>
internal static class JsonObjectMerge
{
    /// <summary>Merges by object property, treating arrays and other values as indivisible.</summary>
    /// <param name="baseline">The version both sets of edits started from.</param>
    /// <param name="session">The current session's version.</param>
    /// <param name="disk">The latest saved version.</param>
    /// <returns>Independent documents that differ only where both sides changed a property differently.</returns>
    internal static (JsonObject KeepSession, JsonObject KeepDisk) Merge(JsonObject baseline, JsonObject session, JsonObject disk) =>
        MergeProperties(baseline, session, disk);

    private static (JsonObject KeepSession, JsonObject KeepDisk) MergeProperties(
        JsonObject? baseline,
        JsonObject session,
        JsonObject disk)
    {
        var keepSession = new JsonObject();
        var keepDisk = new JsonObject();
        var names = disk.Select(property => property.Key).Union(session.Select(property => property.Key), StringComparer.Ordinal);

        foreach (string name in names)
        {
            Property original = ReadProperty(baseline, name);
            Property local = ReadProperty(session, name);
            Property saved = ReadProperty(disk, name);

            if (saved.Matches(original) || local.Matches(saved))
                saved = local;
            else if (local.Matches(original))
                local = saved;
            else if (local.Value is JsonObject localObject && saved.Value is JsonObject savedObject
                && (!original.Exists || original.Value is JsonObject))
            {
                // A newly added object can contain independent edits. Replacing an existing scalar is a conflict.
                (JsonObject localMerged, JsonObject savedMerged) = MergeProperties(original.Value as JsonObject, localObject, savedObject);
                keepSession.Add(name, localMerged);
                keepDisk.Add(name, savedMerged);
                continue;
            }

            if (local.Exists)
                keepSession.Add(name, local.Value?.DeepClone());
            if (saved.Exists)
                keepDisk.Add(name, saved.Value?.DeepClone());
        }

        return (keepSession, keepDisk);
    }

    private static Property ReadProperty(JsonObject? document, string name) =>
        document != null && document.TryGetPropertyValue(name, out JsonNode? value) ? new Property(true, value) : default;

    private readonly record struct Property(bool Exists, JsonNode? Value)
    {
        public bool Matches(Property other) => Exists == other.Exists && JsonNode.DeepEquals(Value, other.Value);
    }
}
