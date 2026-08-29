using System.Text;
using System.Text.Json;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Sets.Serialization;

namespace ThisIsMyPC.Core.Sets;

public sealed class CustomSetWriter : ICustomSetWriter
{
    private const int MaxFileNameAttempts = 1000;

    private readonly string _userDirectory;

    public CustomSetWriter(string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        _userDirectory = userDirectory;
    }

    public CustomSetWriteResult WriteFromPendingGroups(CustomSetMetadata metadata, IReadOnlyList<ChangeGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(groups);

        // One entry per group: a ChangeGroup is one logical toggle whose extra
        // descriptors the module's ISetEntryInspector re-expands at staging time.
        // The schema's toggle-value convention stores the group's first value.
        var entries = new List<SetEntryDocument>();
        var skipped = 0;
        foreach (var group in groups)
        {
            if (group.Changes.Count == 0 || group.Changes[0].AfterValue is null)
            {
                skipped++;
                continue;
            }

            var primary = group.Changes[0];
            entries.Add(new SetEntryDocument
            {
                ModuleId = primary.ModuleId,
                SettingId = primary.SettingId,
                Value = primary.AfterValue,
                Description = string.IsNullOrWhiteSpace(group.Description) ? group.DisplayName : group.Description,
                DisplayValue = primary.AfterDisplay,
            });
        }

        return Write(metadata, entries, skipped);
    }

    public CustomSetWriteResult WriteFromHistory(CustomSetMetadata metadata, IReadOnlyList<ChangeHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(entries);

        // Rows sharing a GroupId were applied as one toggle — collapse them the same
        // way as pending groups. A null GroupId row stands alone.
        var documents = new List<SetEntryDocument>();
        var skipped = 0;
        foreach (var batch in entries.GroupBy(e => e.GroupId ?? $"solo-{e.Id}"))
        {
            // Rowids follow insertion order; query result order within a batch is not
            // guaranteed (all rows share one applied_at), and the schema's toggle
            // convention needs the group's FIRST descriptor's value.
            var primary = batch.MinBy(e => e.Id)!;
            if (primary.AfterValue is null)
            {
                skipped++;
                continue;
            }

            documents.Add(new SetEntryDocument
            {
                ModuleId = primary.ModuleId,
                SettingId = primary.SettingId,
                Value = primary.AfterValue,
                Description = primary.DisplayName,
                DisplayValue = primary.AfterDisplay,
            });
        }

        return Write(metadata, documents, skipped);
    }

    private CustomSetWriteResult Write(CustomSetMetadata metadata, List<SetEntryDocument> entries, int skipped)
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
            return new CustomSetWriteResult { Error = "Set name is required.", SkippedGroupCount = skipped };
        if (string.IsNullOrWhiteSpace(metadata.Description))
            return new CustomSetWriteResult { Error = "Set description is required.", SkippedGroupCount = skipped };
        if (entries.Count == 0)
            return new CustomSetWriteResult { Error = "No changes to save as a set.", SkippedGroupCount = skipped };

        var document = new SetDocument
        {
            Name = metadata.Name.Trim(),
            Description = metadata.Description.Trim(),
            Category = metadata.Category,
            Version = "1.0",
            Author = ResolveAuthor(),
            Entries = entries,
        };

        try
        {
            Directory.CreateDirectory(_userDirectory);
            var path = WriteToUniqueFile(document);
            return new CustomSetWriteResult
            {
                FilePath = path,
                EntryCount = entries.Count,
                SkippedGroupCount = skipped,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CustomSetWriteResult
            {
                Error = $"Could not write the set file: {ex.Message}",
                SkippedGroupCount = skipped,
            };
        }
    }

    private string WriteToUniqueFile(SetDocument document)
    {
        var slug = Slugify(document.Name!);
        for (var attempt = 1; attempt <= MaxFileNameAttempts; attempt++)
        {
            var fileName = attempt == 1 ? $"{slug}.json" : $"{slug}-{attempt}.json";
            var path = Path.Combine(_userDirectory, fileName);

            // CreateNew guarantees an existing file (built by an earlier save with
            // the same name) is never overwritten.
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Name taken — try the next suffix.
                continue;
            }

            try
            {
                using (stream)
                {
                    JsonSerializer.Serialize(stream, document, SetJsonContext.Default.SetDocument);
                }

                return path;
            }
            catch
            {
                // Never leave a half-written set behind for the provider to warn about.
                try { File.Delete(path); } catch (IOException) { }
                throw;
            }
        }

        throw new IOException($"Could not find a free file name for '{slug}' in {_userDirectory}.");
    }

    private static string ResolveAuthor()
    {
        var user = Environment.UserName;
        return string.IsNullOrWhiteSpace(user) ? "user" : user;
    }

    private static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasDash = true; // suppress leading dashes
        foreach (var raw in name.Trim())
        {
            var ch = char.ToLowerInvariant(raw);
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        return slug.Length == 0 ? "custom-set" : slug;
    }
}
