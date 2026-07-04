#pragma once

#include <QtCore>

#include "ScenePlan.h"

class Field;

namespace LLMValidate {

enum class Severity { Info, Warn, Error };

struct Issue {
    Severity level{Severity::Info};
    QString path;          // e.g., "dialog[3].text" or "events[1].steps[0]"
    QString message;       // human-friendly description
};

struct Options {
    int maxLineLength{100};
    bool profanityFilter{false};
    QStringList bannedWords;   // populated by caller when profanityFilter == true
};

struct Result {
    QVector<Issue> issues;
    bool hasErrors() const {
        for (const auto& i : issues) if (i.level == Severity::Error) return true;
        return false;
    }
    int warnCount() const {
        int n=0; for (const auto& i : issues) if (i.level == Severity::Warn) ++n; return n;
    }
};

// Validate a typed ScenePlan against safety rules and current field bounds.
Result validate(const ScenePlan& plan, Field* field, const Options& opts = {});

} // namespace LLMValidate

