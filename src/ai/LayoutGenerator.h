#pragma once

#include <QtCore>

#include "ScenePlan.h"

class Field;
class BackgroundFile;
class IdFile;

struct LayoutOptions {
    int minDistancePx{24};           // minimum separation between actors
    bool enableWalkmeshSnap{false};  // optional projection to nearest triangle centroid
    int nudgeStepPx{8};              // step size when nudging to avoid overlap
    int maxNudgeTries{200};          // cap to avoid infinite search
};

struct LayoutResult {
    bool ok{true};
    QStringList notes;               // adjustments performed
};

// Adjusts actor and prop positions to be inside bounds, avoid overlaps, and optionally snap to walkmesh.
namespace LLMLayout {
    // Computes field pixel bounds; falls back to 320x240 if unavailable.
    QRect fieldBounds(Field* field);

    // Adjusts placements in-place in the ScenePlan; returns summary of changes.
    LayoutResult adjust(ScenePlan& plan, Field* field, const LayoutOptions& opts = {});
}

