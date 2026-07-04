#include "LayoutGenerator.h"

#include "core/field/Field.h"
#include "core/field/BackgroundFile.h"
#include "core/field/BackgroundTiles.h"
#include "core/field/IdFile.h"

namespace LLMLayout {

static double dist2(const QPoint& a, const QPoint& b) {
    const int dx = a.x() - b.x();
    const int dy = a.y() - b.y();
    return double(dx)*dx + double(dy)*dy;
}

static QPoint clampPoint(const QPoint& p, const QRect& bounds) {
    int x = qBound(bounds.left(), p.x(), bounds.right());
    int y = qBound(bounds.top(), p.y(), bounds.bottom());
    return QPoint(x, y);
}

// Simple spiral-ish nudge search
static QPoint nudgeToFree(const QPoint& start,
                          const QRect& bounds,
                          const QVector<QPoint>& placed,
                          int minDist,
                          int step,
                          int maxTries)
{
    const int minDist2 = minDist * minDist;
    auto freePos = [&](const QPoint& pt) {
        for (const auto& q : placed) {
            if (dist2(pt, q) < minDist2) return false;
        }
        return bounds.contains(pt);
    };

    if (freePos(start)) return start;

    QPoint p = start;
    int dirIndex = 0;
    const QPoint dirs[8] = { {1,0}, {1,1}, {0,1}, {-1,1}, {-1,0}, {-1,-1}, {0,-1}, {1,-1} };
    int tries = 0;
    int legLen = 1;
    int legProgress = 0;
    int legRepeats = 0;
    while (tries++ < maxTries) {
        // advance
        p += dirs[dirIndex] * step;
        p = clampPoint(p, bounds);
        if (freePos(p)) return p;
        // manage spiral: two legs per legLen, then increase
        if (++legProgress >= legLen) {
            legProgress = 0;
            dirIndex = (dirIndex + 1) % 8;
            if (++legRepeats == 2) { legRepeats = 0; ++legLen; }
        }
    }
    // give up: return start clamped
    return clampPoint(start, bounds);
}

QRect fieldBounds(Field* field)
{
    if (!field) return QRect(0,0,320,240);
    BackgroundFile* bg = field->background(true);
    if (!bg) return QRect(0,0,320,240);
    const auto rect = bg->tiles().rect();
    if (rect.isValid() && rect.width() > 0 && rect.height() > 0) return rect;
    return QRect(0,0,320,240);
}

static bool hasWalkmesh(Field* field) {
    if (!field) return false;
    IdFile* id = field->walkmesh(true);
    return id && id->hasTriangle() && id->triangleCount() > 0;
}

static QPoint snapToWalkmesh(const QPoint& p, Field* field)
{
    IdFile* id = field ? field->walkmesh(true) : nullptr;
    if (!id) return p;
    // Choose nearest triangle centroid in XY plane
    const auto& tris = id->triangles();
    if (tris.isEmpty()) return p;
    double bestD2 = std::numeric_limits<double>::max();
    QPoint best = p;
    for (const auto& t : tris) {
        const auto& v0 = t.vertices[0];
        const auto& v1 = t.vertices[1];
        const auto& v2 = t.vertices[2];
        const QPoint c((v0.x + v1.x + v2.x) / 3, (v0.y + v1.y + v2.y) / 3);
        const double d2 = dist2(c, p);
        if (d2 < bestD2) { bestD2 = d2; best = c; }
    }
    return best;
}

LayoutResult adjust(ScenePlan& plan, Field* field, const LayoutOptions& opts)
{
    LayoutResult res;
    const QRect bounds = fieldBounds(field);

    // Adjust spawn point
    if (!bounds.contains(plan.layout.spawnPoint)) {
        const QPoint before = plan.layout.spawnPoint;
        plan.layout.spawnPoint = clampPoint(plan.layout.spawnPoint, bounds);
        res.notes << QStringLiteral("Clamped spawnPoint from %1,%2 to %3,%4")
                     .arg(before.x()).arg(before.y())
                     .arg(plan.layout.spawnPoint.x()).arg(plan.layout.spawnPoint.y());
    }

    // Clamp props and record their bounds as obstacles
    QVector<QPoint> obstacles; obstacles.reserve(plan.layout.props.size());
    for (auto& prop : plan.layout.props) {
        if (!bounds.contains(prop.position)) {
            const QPoint before = prop.position;
            prop.position = clampPoint(prop.position, bounds);
            res.notes << QStringLiteral("Clamped prop '%1' from %2,%3 to %4,%5")
                         .arg(prop.id).arg(before.x()).arg(before.y())
                         .arg(prop.position.x()).arg(prop.position.y());
        }
        obstacles.append(prop.position);
    }

    // Ensure spawn not intersect props (within minDistance)
    for (const auto& ob : obstacles) {
        if (dist2(plan.layout.spawnPoint, ob) < double(opts.minDistancePx) * opts.minDistancePx) {
            const QPoint before = plan.layout.spawnPoint;
            // nudge spawn
            QVector<QPoint> placed = obstacles; // treat props as already placed
            plan.layout.spawnPoint = nudgeToFree(plan.layout.spawnPoint, bounds, placed,
                                                 opts.minDistancePx, opts.nudgeStepPx, opts.maxNudgeTries);
            res.notes << QStringLiteral("Nudged spawnPoint from %1,%2 to %3,%4 to avoid props")
                         .arg(before.x()).arg(before.y())
                         .arg(plan.layout.spawnPoint.x()).arg(plan.layout.spawnPoint.y());
            break;
        }
    }

    // Adjust actors: clamp → optional walkmesh snap → avoid overlaps
    QVector<QPoint> placed; placed.reserve(plan.actors.size());
    for (auto& actor : plan.actors) {
        QPoint pos = actor.position;
        // Clamp to bounds
        if (!bounds.contains(pos)) {
            const QPoint before = pos;
            pos = clampPoint(pos, bounds);
            res.notes << QStringLiteral("Clamped actor '%1' from %2,%3 to %4,%5")
                         .arg(actor.id).arg(before.x()).arg(before.y())
                         .arg(pos.x()).arg(pos.y());
        }

        // Snap to walkmesh centroid if enabled and available
        if (opts.enableWalkmeshSnap && hasWalkmesh(field)) {
            const QPoint before = pos;
            pos = snapToWalkmesh(pos, field);
            if (pos != before) {
                // Clamp again after snap
                pos = clampPoint(pos, bounds);
                res.notes << QStringLiteral("Snapped actor '%1' to walkmesh at %2,%3")
                             .arg(actor.id).arg(pos.x()).arg(pos.y());
            }
        }

        // Avoid overlaps with already placed actors and props (obstacles)
        QVector<QPoint> occupied = placed;
        occupied += obstacles;
        occupied.append(plan.layout.spawnPoint);
        const QPoint freePos = nudgeToFree(pos, bounds, occupied, opts.minDistancePx, opts.nudgeStepPx, opts.maxNudgeTries);
        if (freePos != pos) {
            res.notes << QStringLiteral("Nudged actor '%1' from %2,%3 to %4,%5 to avoid overlaps")
                         .arg(actor.id).arg(pos.x()).arg(pos.y())
                         .arg(freePos.x()).arg(freePos.y());
        }
        actor.position = freePos;
        placed.append(freePos);
    }

    return res;
}

} // namespace LLMLayout

