#pragma once

#include <QtCore>

#include "ScenePlan.h"

class Field;
class Section1File;
class GrpScript;
class Script;

// Maps a ScenePlan onto a Field (scripts/texts/groups).
// Initial implementation focuses on scaffolding and idempotent grouping.
class ScenePlanMapper {
public:
    struct ApplyOptions {
        bool previewOnly{true};
        QString groupNameOverride{};
    };

    struct ApplyResult {
        bool ok{false};
        QString groupName;                    // created or planned group name
        QString summary;                      // human-readable summary for preview
        QString error;                        // on failure
    };

    static ApplyResult applyToField(const ScenePlan& plan, Field* field, const ApplyOptions& opts);

private:
    static QString makeGroupName(const QString& base = QString());
    static QString summarize(const ScenePlan& plan);
};
