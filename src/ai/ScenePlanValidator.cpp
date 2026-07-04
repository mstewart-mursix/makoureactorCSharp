#include "ScenePlanValidator.h"

#include "core/field/Field.h"
#include "ai/LayoutGenerator.h"

namespace LLMValidate {

static void add(QVector<Issue>& v, Severity lvl, const QString& path, const QString& msg) {
    v.append(Issue{lvl, path, msg});
}

static void checkText(QVector<Issue>& v, const QString& path, const QString& text, int maxLen,
                      bool profanityFilter, const QStringList& banned)
{
    // Control chars except TAB/CR/LF are not allowed
    for (int i=0;i<text.size();++i) {
        const QChar c = text.at(i);
        if (c.unicode() < 0x20 && c != '\n' && c != '\r' && c != '\t') {
            add(v, Severity::Error, path, QStringLiteral("Text contains control character at index %1").arg(i));
            break;
        }
    }
    // Line length
    const auto lines = text.split('\n');
    for (int li=0; li<lines.size(); ++li) {
        const QString& line = lines.at(li);
        if (line.size() > maxLen) {
            add(v, Severity::Error, path, QStringLiteral("Line %1 exceeds %2 characters (%3)")
                                          .arg(li+1).arg(maxLen).arg(line.size()));
        }
    }
    // Profanity (very basic): lowercase containment against provided list
    if (profanityFilter && !banned.isEmpty()) {
        const QString lower = text.toLower();
        for (const auto& w : banned) {
            if (!w.isEmpty() && lower.contains(w.toLower())) {
                add(v, Severity::Warn, path, QStringLiteral("Contains flagged term: '%1'").arg(w));
                // do not break, report all
            }
        }
    }
}

static void collectActorIds(const ScenePlan& plan, QSet<QString>& ids)
{
    for (const auto& a : plan.actors) ids.insert(a.id);
}

static void validateEvents(const ScenePlan& plan, Field* field, const Options& opts, QVector<Issue>& out)
{
    const QRect bounds = LLMLayout::fieldBounds(field);
    QSet<QString> actorIds; collectActorIds(plan, actorIds);

    for (int i=0;i<plan.events.size();++i) {
        const auto& e = plan.events[i];
        const QString epath = QStringLiteral("events[%1]").arg(i);
        if (e.trigger == "zone") {
            if (!bounds.contains(e.triggerZone)) {
                add(out, Severity::Error, epath + ".triggerZone",
                    QStringLiteral("Trigger zone out of bounds (%1,%2 %3x%4)")
                        .arg(e.triggerZone.x()).arg(e.triggerZone.y())
                        .arg(e.triggerZone.width()).arg(e.triggerZone.height()));
            }
        }
        for (int j=0;j<e.steps.size();++j) {
            const auto& s = e.steps[j];
            const QString spath = QStringLiteral("%1.steps[%2]").arg(epath).arg(j);
            using T = EventStep::Type;
            switch (s.type) {
            case T::Say:
                if (!actorIds.contains(s.actorId) && s.actorId != "narrator") {
                    add(out, Severity::Error, spath + ".actorId",
                        QStringLiteral("Unknown actor '%1'").arg(s.actorId));
                }
                checkText(out, spath + ".text", s.text, opts.maxLineLength, opts.profanityFilter, opts.bannedWords);
                break;
            case T::Move:
            case T::Face:
                if (!actorIds.contains(s.actorId)) {
                    add(out, Severity::Error, spath + ".actorId",
                        QStringLiteral("Unknown actor '%1'").arg(s.actorId));
                }
                break;
            case T::Wait:
                if (s.ms < 0) {
                    add(out, Severity::Error, spath + ".ms", QStringLiteral("Negative wait time"));
                }
                break;
            case T::PlayMusic:
                if (s.track.trimmed().isEmpty()) {
                    add(out, Severity::Warn, spath + ".track", QStringLiteral("Empty music track id"));
                }
                break;
            case T::SetFlag:
                if (s.key.trimmed().isEmpty()) {
                    add(out, Severity::Error, spath + ".key", QStringLiteral("Empty flag key"));
                }
                break;
            case T::IfFlag:
                if (s.key.trimmed().isEmpty()) {
                    add(out, Severity::Error, spath + ".key", QStringLiteral("Empty flag key"));
                }
                // recursively validate nested steps
                {
                    ScenePlan tmp; // reuse structure to call validator on nested inlined manner
                    EventDef ed; ed.id = e.id + ":then"; ed.trigger = e.trigger; ed.steps = s.thenSteps;
                    tmp.events = { ed };
                    validateEvents(tmp, field, opts, out);
                }
                if (!s.elseSteps.isEmpty()) {
                    ScenePlan tmp; EventDef ed; ed.id = e.id + ":else"; ed.trigger = e.trigger; ed.steps = s.elseSteps; tmp.events = { ed };
                    validateEvents(tmp, field, opts, out);
                }
                break;
            case T::GiveItem:
                if (s.qty <= 0) {
                    add(out, Severity::Error, spath + ".qty", QStringLiteral("Quantity must be > 0"));
                }
                if (s.itemId.trimmed().isEmpty()) {
                    add(out, Severity::Error, spath + ".itemId", QStringLiteral("Empty item id"));
                }
                break;
            case T::Battle:
                if (s.encounterId.trimmed().isEmpty()) {
                    add(out, Severity::Error, spath + ".encounterId", QStringLiteral("Empty encounter id"));
                }
                break;
            case T::CustomNote:
                checkText(out, spath + ".text", s.text, opts.maxLineLength, opts.profanityFilter, opts.bannedWords);
                break;
            }
        }
    }
}

static void validateDialog(const ScenePlan& plan, const Options& opts, const QSet<QString>& actorIds, QVector<Issue>& out)
{
    for (int i=0;i<plan.dialog.size();++i) {
        const auto& d = plan.dialog[i];
        const QString path = QStringLiteral("dialog[%1]").arg(i);
        if (!(d.speakerId == "narrator" || actorIds.contains(d.speakerId))) {
            add(out, Severity::Error, path + ".speakerId",
                QStringLiteral("Unknown speaker '%1'").arg(d.speakerId));
        }
        checkText(out, path + ".text", d.text, opts.maxLineLength, opts.profanityFilter, opts.bannedWords);
    }
}

static void validateActors(const ScenePlan& plan, Field* field, QVector<Issue>& out)
{
    const QRect bounds = LLMLayout::fieldBounds(field);
    QSet<QString> seen;
    for (int i=0;i<plan.actors.size();++i) {
        const auto& a = plan.actors[i];
        const QString path = QStringLiteral("actors[%1]").arg(i);
        if (a.id.trimmed().isEmpty()) {
            add(out, Severity::Error, path + ".id", QStringLiteral("Empty actor id"));
        } else if (seen.contains(a.id)) {
            add(out, Severity::Error, path + ".id", QStringLiteral("Duplicate actor id '%1'").arg(a.id));
        } else {
            seen.insert(a.id);
        }
        if (!bounds.contains(a.position)) {
            add(out, Severity::Warn, path + ".position",
                QStringLiteral("Position out of bounds (%1,%2)")
                    .arg(a.position.x()).arg(a.position.y()));
        }
        if (!a.facing.isNull()) {
            const QChar c = a.facing;
            if (!(c == 'N' || c == 'S' || c == 'E' || c == 'W')) {
                add(out, Severity::Warn, path + ".facing",
                    QStringLiteral("Invalid facing '%1' (expected N/S/E/W)").arg(QString(c)));
            }
        }
    }
}

static void validateLayout(const ScenePlan& plan, Field* field, QVector<Issue>& out)
{
    const QRect bounds = LLMLayout::fieldBounds(field);
    if (!bounds.contains(plan.layout.spawnPoint)) {
        add(out, Severity::Warn, QStringLiteral("layout.spawnPoint"),
            QStringLiteral("Spawn point out of bounds (%1,%2)")
                .arg(plan.layout.spawnPoint.x()).arg(plan.layout.spawnPoint.y()));
    }
    for (int i=0;i<plan.layout.props.size();++i) {
        const auto& p = plan.layout.props[i];
        if (!bounds.contains(p.position)) {
            add(out, Severity::Warn, QStringLiteral("layout.props[%1].position").arg(i),
                QStringLiteral("Prop '%1' out of bounds (%2,%3)")
                    .arg(p.id).arg(p.position.x()).arg(p.position.y()));
        }
    }
}

Result validate(const ScenePlan& plan, Field* field, const Options& opts)
{
    Result r;
    // Actors (duplicates, bounds)
    validateActors(plan, field, r.issues);

    // Dialog (speaker exists, text checks)
    QSet<QString> actorIds; collectActorIds(plan, actorIds);
    validateDialog(plan, opts, actorIds, r.issues);

    // Events (actor references, zones in bounds, step specifics)
    validateEvents(plan, field, opts, r.issues);

    // Layout props and spawn bounds
    validateLayout(plan, field, r.issues);

    return r;
}

} // namespace LLMValidate

