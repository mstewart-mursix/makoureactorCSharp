using FluentAssertions;

using MakouReactor.AI;
using MakouReactor.AI.History;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.History;

public sealed class GenerationHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr_history_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static GenerationHistoryEntry Entry(string prompt, DateTime at) => new()
    {
        Prompt = prompt,
        RawJson = "{}",
        CreatedUtc = at,
    };

    [Fact]
    public void add_assigns_id_and_list_returns_entries_newest_first()
    {
        var store = new GenerationHistoryStore(_dir);
        var t = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        store.Add(Entry("first", t));
        store.Add(Entry("second", t.AddMinutes(1)));
        store.Add(Entry("third", t.AddMinutes(2)));

        var list = store.List();
        list.Select(e => e.Prompt).Should().Equal("third", "second", "first");
        list.Should().OnlyContain(e => e.Id.Length > 0);
        list.Select(e => e.Id).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void entries_round_trip_all_fields()
    {
        var store = new GenerationHistoryStore(_dir);
        var added = store.Add(new GenerationHistoryEntry
        {
            Prompt = "p", GenDialog = false, GenLayout = true, GenScripts = false, FieldName = "md1stin",
            Refinement = "angrier", RawJson = "{\"a\":1}", Attempts = 2, ValidationErrors = 1, ValidationWarnings = 3,
        });

        var loaded = store.List().Single();

        loaded.Should().BeEquivalentTo(added);
    }

    [Fact]
    public void list_is_capped_by_pruning_the_oldest()
    {
        var store = new GenerationHistoryStore(_dir, maxEntries: 3);
        var t = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 6; i++)
            store.Add(Entry($"e{i}", t.AddMinutes(i)));

        store.List().Select(e => e.Prompt).Should().Equal("e5", "e4", "e3");
        Directory.GetFiles(_dir, "*.json").Should().HaveCount(3);
    }

    [Fact]
    public void default_cap_is_fifty()
    {
        new GenerationHistoryStore(_dir).MaxEntries.Should().Be(50);
    }

    [Fact]
    public void corrupt_files_are_skipped_and_do_not_break_listing()
    {
        var store = new GenerationHistoryStore(_dir);
        store.Add(Entry("good", DateTime.UtcNow));
        File.WriteAllText(Path.Combine(_dir, "garbage.json"), "{ this is not json");
        File.WriteAllText(Path.Combine(_dir, "empty.json"), "null");

        store.List().Select(e => e.Prompt).Should().Equal("good");
    }

    [Fact]
    public void delete_and_clear_remove_entries()
    {
        var store = new GenerationHistoryStore(_dir);
        var a = store.Add(Entry("a", DateTime.UtcNow));
        store.Add(Entry("b", DateTime.UtcNow.AddSeconds(1)));

        store.Delete(a.Id).Should().BeTrue();
        store.Delete(a.Id).Should().BeFalse();
        store.List().Should().ContainSingle();

        store.Clear();
        store.List().Should().BeEmpty();
    }

    [Fact]
    public void list_on_a_missing_directory_is_empty()
    {
        new GenerationHistoryStore(Path.Combine(_dir, "nope")).List().Should().BeEmpty();
    }

    [Fact]
    public void an_id_cannot_escape_the_history_folder()
    {
        var store = new GenerationHistoryStore(_dir);
        var outside = Path.Combine(Path.GetTempPath(), "mr_outside_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outside, "x");
        try
        {
            store.Delete("../" + Path.GetFileNameWithoutExtension(outside)).Should().BeFalse();
            File.Exists(outside).Should().BeTrue();
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public void from_captures_a_successful_result_and_a_failure()
    {
        var prompt = new ScenePrompt { UserText = "Scene", GenScripts = false };
        var ok = new SceneGenerationResult
        {
            Ok = true,
            RawJson = "{\"x\":1}",
            Attempts = new[] { "err", string.Empty },
            Validation = new ValidationResult
            {
                Issues =
                {
                    new Issue(Severity.Warn, "a", "w"),
                    new Issue(Severity.Warn, "b", "w"),
                    new Issue(Severity.Error, "c", "e"),
                },
            },
        };

        var success = GenerationHistoryEntry.From(prompt, ok, "md1stin");
        var failure = GenerationHistoryEntry.From(prompt, SceneGenerationResult.Failure("boom"), refinement: "again");

        success.Succeeded.Should().BeTrue();
        success.Attempts.Should().Be(2);
        success.ValidationWarnings.Should().Be(2);
        success.ValidationErrors.Should().Be(1);
        success.GenScripts.Should().BeFalse();
        success.FieldName.Should().Be("md1stin");
        failure.Succeeded.Should().BeFalse();
        failure.Error.Should().Be("boom");
        failure.Title.Should().Contain("Refine: again");
    }
}
