#include "ScenePlanMapper.h"

#include <QDateTime>

#ifndef LLM_MAPPER_PREVIEW_ONLY
#include "core/field/Field.h"
#include "core/field/Section1File.h"
#include "core/field/GrpScript.h"
#include "core/field/Script.h"
#endif

ScenePlanMapper::ApplyResult ScenePlanMapper::applyToField(const ScenePlan& plan, Field* field, const ApplyOptions& opts)
{
    ApplyResult out;
    if (!field) { out.error = QStringLiteral("No active field"); return out; }

    const QString groupName = opts.groupNameOverride.isEmpty() ? makeGroupName(QStringLiteral("LLM_Generated"))
                                                              : opts.groupNameOverride;

    // Build a summary for preview
    out.summary = summarize(plan);

    if (opts.previewOnly) {
        out.ok = true;
        out.groupName = groupName;
        return out;
    }

#ifdef LLM_MAPPER_PREVIEW_ONLY
    out.error = QStringLiteral("Mapper built in preview-only mode");
    return out;
#else
    // Apply changes: create group and basic scripts under the new group.
    // NOTE: Minimal scaffolding only; detailed opcode emission will be incrementally added.
    Section1File* s1 = field->scriptsAndTexts(true);
    if (!s1) { out.error = QStringLiteral("Unable to open field scripts/texts section"); return out; }

    // Create empty scripts set for the new group (SCRIPTS_SIZE entries)
    QList<Script> scripts;
    scripts.reserve(SCRIPTS_SIZE);
    for (int i=0;i<SCRIPTS_SIZE;++i) scripts.append(Script());

    GrpScript grp(groupName, scripts);

    // Place a NOP in script #0 to ensure non-empty group
    Script scr;
    Opcode nop(OpcodeKey::NOP, nullptr, 0);
    scr.insertOpcode(0, nop);
    grp.setScript(0, scr);

    // Insert at end, respecting capacity
    if (s1->grpScriptCount() >= Section1File::maxGrpScriptCount()) {
        out.error = QStringLiteral("Maximum number of script groups reached");
        return out;
    }
    if (!s1->insertGrpScript(int(s1->grpScriptCount()), grp)) {
        out.error = QStringLiteral("Failed to insert new script group");
        return out;
    }

    field->setModified(true);

    out.ok = true;
    out.groupName = groupName;
    return out;
#endif
}

QString ScenePlanMapper::makeGroupName(const QString& base)
{
    const auto ts = QDateTime::currentDateTime().toString("yyyyMMdd_HHmmss");
    return base.isEmpty() ? QStringLiteral("LLM_Generated_%1").arg(ts)
                          : QStringLiteral("%1_%2").arg(base, ts);
}

QString ScenePlanMapper::summarize(const ScenePlan& plan)
{
    QString s;
    s += QStringLiteral("Title: %1\nModel: %2 (schema %3)\n")
            .arg(plan.meta.title, plan.meta.model, plan.meta.version);
    s += QStringLiteral("Actors: %1\nDialog lines: %2\nEvents: %3\n")
            .arg(plan.actors.size()).arg(plan.dialog.size()).arg(plan.events.size());

    if (!plan.actors.isEmpty()) {
        s += QStringLiteral("\nActors:\n");
        for (const auto& a : plan.actors) {
            const auto pos = a.position;
            const auto face = a.facing.isNull() ? QLatin1String("?") : QString(a.facing);
            s += QStringLiteral(" - %1 (%2) at %3,%4 facing %5\n")
                    .arg(a.id, a.displayName.isEmpty() ? a.id : a.displayName)
                    .arg(pos.x()).arg(pos.y()).arg(face);
        }
    }
    if (!plan.dialog.isEmpty()) {
        s += QStringLiteral("\nDialog (first 5):\n");
        const int n = qMin(5, plan.dialog.size());
        for (int i=0;i<n;++i) {
            const auto& d = plan.dialog[i];
            s += QStringLiteral(" - %1: %2\n").arg(d.speakerId, d.text.left(80));
        }
        if (plan.dialog.size() > n) s += QStringLiteral(" (… %1 more)\n").arg(plan.dialog.size() - n);
    }
    if (!plan.events.isEmpty()) {
        s += QStringLiteral("\nEvents:\n");
        for (const auto& e : plan.events) {
            s += QStringLiteral(" - %1 [%2], steps=%3\n").arg(e.id, e.trigger).arg(e.steps.size());
        }
    }
    return s;
}
