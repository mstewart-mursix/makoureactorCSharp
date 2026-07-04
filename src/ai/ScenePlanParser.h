#pragma once

#include <QtCore>

#include "ScenePlan.h"

// Parses JSON (from LM Studio result) into ScenePlan.
// Strict typing; emits detailed path-based errors.
class ScenePlanParser {
public:
    struct Result {
        bool ok{false};
        ScenePlan plan;
        QString error;      // Human-friendly message with JSON path and expectation
    };

    static Result parse(const QByteArray& jsonBytes);
};

