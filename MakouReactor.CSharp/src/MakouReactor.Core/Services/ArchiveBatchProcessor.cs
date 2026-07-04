using System.Threading;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.Core.Services;

[Flags]
public enum ArchiveBatchOperation
{
    None = 0,
    CleanUnusedTexts = 1,
    EmptyTexts = 2,
    DisableBattles = 4,
    CleanModelLoader = 8,
    RemoveUnusedBackgroundSections = 16,
    AutosizeTextWindows = 32,
    ResizeBackgrounds = 64,
    RepairBackgrounds = 128,
}

public sealed record ArchiveBatchResult(
    int FieldsVisited,
    int FieldsChanged,
    int FieldsSkipped,
    int TextEntriesChanged,
    int TextWindowsAutosized,
    int EncounterTablesChanged,
    int ModelLoaderEntriesChanged,
    int BackgroundSectionsRemoved,
    int BackgroundResizeTilesAdded,
    int BackgroundTilesRepaired,
    IReadOnlyList<string> Errors);

public sealed class ArchiveBatchProcessor
{
    public ArchiveBatchResult Apply(
        FieldArchive archive,
        ArchiveBatchOperation operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (operations == ArchiveBatchOperation.None)
            return new ArchiveBatchResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);

        var fieldsVisited = 0;
        var fieldsChanged = 0;
        var fieldsSkipped = 0;
        var textEntriesChanged = 0;
        var textWindowsAutosized = 0;
        var encounterTablesChanged = 0;
        var modelLoaderEntriesChanged = 0;
        var backgroundSectionsRemoved = 0;
        var backgroundResizeTilesAdded = 0;
        var backgroundTilesRepaired = 0;
        var errors = new List<string>();

        foreach (var entry in archive.FieldEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fieldsVisited++;

            FieldPC field;
            try
            {
                field = archive.OpenField(entry.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fieldsSkipped++;
                errors.Add($"{entry.Name}: {ex.Message}");
                continue;
            }

            var fieldChanged = false;
            var before = textEntriesChanged;
            var section = field.ScriptsAndTexts;
            if (section != null && operations.HasFlag(ArchiveBatchOperation.CleanUnusedTexts))
                textEntriesChanged += section.CleanUnusedTexts();
            if (section != null && operations.HasFlag(ArchiveBatchOperation.EmptyTexts))
                textEntriesChanged += section.EmptyTexts();
            fieldChanged |= textEntriesChanged != before;

            if (section != null && operations.HasFlag(ArchiveBatchOperation.AutosizeTextWindows))
            {
                var windowChanges = section.AutosizeTextWindows();
                if (windowChanges > 0)
                {
                    textWindowsAutosized += windowChanges;
                    fieldChanged = true;
                }
            }

            if (operations.HasFlag(ArchiveBatchOperation.DisableBattles) &&
                field.Encounters != null)
            {
                for (var i = 0; i < field.Encounters.Tables.Count; i++)
                {
                    var table = field.Encounters.GetTable(i);
                    if (!table.Enabled && table.Rate == 0)
                        continue;

                    field.Encounters.SetTable(i, table with
                    {
                        Enabled = false,
                        Rate = 0,
                    });
                    encounterTablesChanged++;
                    fieldChanged = true;
                }

                if (fieldChanged)
                    field.ApplyEncounterChanges();
            }

            if (operations.HasFlag(ArchiveBatchOperation.CleanModelLoader) &&
                field.ModelLoader != null)
            {
                var modelChanges = field.ModelLoader.CleanUnusedData();
                if (modelChanges > 0)
                {
                    modelLoaderEntriesChanged += modelChanges;
                    field.ApplyModelLoaderChanges();
                    fieldChanged = true;
                }
            }

            if (operations.HasFlag(ArchiveBatchOperation.RemoveUnusedBackgroundSections) &&
                field.RemoveUnusedTilesSection())
            {
                backgroundSectionsRemoved++;
                fieldChanged = true;
            }

            if (operations.HasFlag(ArchiveBatchOperation.ResizeBackgrounds) &&
                field.Background != null)
            {
                var resize = field.ResizeBackgroundToMinimumWidth(448);
                if (resize.Changed)
                {
                    backgroundResizeTilesAdded += resize.TilesAdded;
                    fieldChanged = true;
                }
            }

            if (operations.HasFlag(ArchiveBatchOperation.RepairBackgrounds) &&
                field.Background != null)
            {
                var repair = field.RepairBackgroundPaletteReferences();
                if (repair.Changed)
                {
                    backgroundTilesRepaired += repair.TilesRepaired;
                    fieldChanged = true;
                }
            }

            if (!fieldChanged)
                continue;

            field.SetModified();
            archive.SaveField(field, cancellationToken);
            fieldsChanged++;
        }

        return new ArchiveBatchResult(
            fieldsVisited,
            fieldsChanged,
            fieldsSkipped,
            textEntriesChanged,
            textWindowsAutosized,
            encounterTablesChanged,
            modelLoaderEntriesChanged,
            backgroundSectionsRemoved,
            backgroundResizeTilesAdded,
            backgroundTilesRepaired,
            errors);
    }
}
