#include "ScenePlanParser.h"

using Err = ScenePlanParser::Result;

namespace {
    QString typeName(const QJsonValue& v) {
        switch (v.type()) {
        case QJsonValue::Null: return QStringLiteral("null");
        case QJsonValue::Bool: return QStringLiteral("bool");
        case QJsonValue::Double: return QStringLiteral("number");
        case QJsonValue::String: return QStringLiteral("string");
        case QJsonValue::Array: return QStringLiteral("array");
        case QJsonValue::Object: return QStringLiteral("object");
        case QJsonValue::Undefined: default: return QStringLiteral("undefined");
        }
    }

    Err errorAt(const QString& path, const QString& expected, const QJsonValue& got) {
        Err r; r.ok = false;
        r.error = QStringLiteral("Parse error at %1: expected %2, got %3")
                    .arg(path, expected, typeName(got));
        return r;
    }

    inline bool isFaceDir(const QString& s) {
        return s == "N" || s == "S" || s == "E" || s == "W";
    }

    inline bool isTrigger(const QString& s) {
        return s == "on_enter" || s == "on_interact" || s == "auto" || s == "zone";
    }

    inline bool isPose(const QString& s) {
        return s == "idle" || s == "talk" || s == "walk";
    }
}

static bool getString(const QJsonObject& o, const QString& key, QString& out) {
    auto it = o.find(key);
    if (it == o.end()) return false;
    if (!it->isString()) return false;
    out = it->toString();
    return true;
}

static bool getObject(const QJsonObject& o, const QString& key, QJsonObject& out) {
    auto it = o.find(key);
    if (it == o.end()) return false;
    if (!it->isObject()) return false;
    out = it->toObject();
    return true;
}

static bool getArray(const QJsonObject& o, const QString& key, QJsonArray& out) {
    auto it = o.find(key);
    if (it == o.end()) return false;
    if (!it->isArray()) return false;
    out = it->toArray();
    return true;
}

static QPoint parsePoint(const QJsonValue& v, const QString& path, Err& err) {
    if (!v.isObject()) { err = errorAt(path, "object {x:number,y:number}", v); return {}; }
    auto o = v.toObject();
    auto vx = o.value("x");
    auto vy = o.value("y");
    if (!vx.isDouble() || !vy.isDouble()) { err = errorAt(path, "{x:number,y:number}", v); return {}; }
    return QPoint(int(vx.toDouble()), int(vy.toDouble()));
}

static QRect parseRect(const QJsonValue& v, const QString& path, Err& err) {
    if (!v.isObject()) { err = errorAt(path, "object {x:number,y:number,w:number,h:number}", v); return {}; }
    auto o = v.toObject();
    auto vx = o.value("x");
    auto vy = o.value("y");
    auto vw = o.value("w");
    auto vh = o.value("h");
    if (!vx.isDouble() || !vy.isDouble() || !vw.isDouble() || !vh.isDouble()) {
        err = errorAt(path, "{x:number,y:number,w:number,h:number}", v); return {};
    }
    return QRect(int(vx.toDouble()), int(vy.toDouble()), int(vw.toDouble()), int(vh.toDouble()));
}

static EventStep parseStep(const QJsonValue& v, const QString& path, Err& err) {
    if (!v.isObject()) { err = errorAt(path, "EventStep object", v); return {}; }
    auto o = v.toObject();
    const auto tval = o.value("type");
    if (!tval.isString()) { err = errorAt(path + ".type", "string", tval); return {}; }
    const QString t = tval.toString();

    EventStep s;
    if (t == "say") {
        s.type = EventStep::Type::Say;
        auto aid = o.value("actorId");
        auto txt = o.value("text");
        if (!aid.isString()) { err = errorAt(path + ".actorId", "string", aid); return {}; }
        if (!txt.isString()) { err = errorAt(path + ".text", "string", txt); return {}; }
        s.actorId = aid.toString();
        s.text = txt.toString();
        return s;
    }
    if (t == "move") {
        s.type = EventStep::Type::Move;
        auto aid = o.value("actorId");
        if (!aid.isString()) { err = errorAt(path + ".actorId", "string", aid); return {}; }
        s.actorId = aid.toString();
        s.to = parsePoint(o.value("to"), path + ".to", err);
        if (!err.ok && !err.error.isEmpty()) return {};
        if (o.contains("speed")) {
            auto sp = o.value("speed");
            if (!sp.isDouble()) { err = errorAt(path + ".speed", "number", sp); return {}; }
            s.speed = sp.toDouble();
        }
        return s;
    }
    if (t == "face") {
        s.type = EventStep::Type::Face;
        auto aid = o.value("actorId");
        auto dir = o.value("dir");
        if (!aid.isString()) { err = errorAt(path + ".actorId", "string", aid); return {}; }
        if (!dir.isString() || !isFaceDir(dir.toString())) {
            err = errorAt(path + ".dir", "\"N\"|\"S\"|\"E\"|\"W\"", dir); return {};
        }
        s.actorId = aid.toString();
        s.dir = dir.toString().at(0);
        return s;
    }
    if (t == "wait") {
        s.type = EventStep::Type::Wait;
        auto ms = o.value("ms");
        if (!ms.isDouble()) { err = errorAt(path + ".ms", "number", ms); return {}; }
        s.ms = int(ms.toDouble());
        return s;
    }
    if (t == "play_music") {
        s.type = EventStep::Type::PlayMusic;
        auto tr = o.value("track");
        if (!tr.isString()) { err = errorAt(path + ".track", "string", tr); return {}; }
        s.track = tr.toString();
        return s;
    }
    if (t == "set_flag") {
        s.type = EventStep::Type::SetFlag;
        auto key = o.value("key");
        auto val = o.value("value");
        if (!key.isString()) { err = errorAt(path + ".key", "string", key); return {}; }
        if (!val.isBool()) { err = errorAt(path + ".value", "boolean", val); return {}; }
        s.key = key.toString();
        s.value = val.toBool();
        return s;
    }
    if (t == "if_flag") {
        s.type = EventStep::Type::IfFlag;
        auto key = o.value("key");
        auto thenv = o.value("then");
        if (!key.isString()) { err = errorAt(path + ".key", "string", key); return {}; }
        if (!thenv.isArray()) { err = errorAt(path + ".then", "EventStep[]", thenv); return {}; }
        s.key = key.toString();
        const auto thenArr = thenv.toArray();
        for (int i=0;i<thenArr.size();++i) {
            EventStep child = parseStep(thenArr.at(i), QString("%1.then[%2]").arg(path).arg(i), err);
            if (!err.ok && !err.error.isEmpty()) return {};
            s.thenSteps.append(child);
        }
        if (o.contains("else")) {
            auto elsev = o.value("else");
            if (!elsev.isArray()) { err = errorAt(path + ".else", "EventStep[]", elsev); return {}; }
            const auto arr = elsev.toArray();
            for (int i=0;i<arr.size();++i) {
                EventStep child = parseStep(arr.at(i), QString("%1.else[%2]").arg(path).arg(i), err);
                if (!err.ok && !err.error.isEmpty()) return {};
                s.elseSteps.append(child);
            }
        }
        return s;
    }
    if (t == "give_item") {
        s.type = EventStep::Type::GiveItem;
        auto item = o.value("itemId");
        auto qty = o.value("qty");
        if (!item.isString()) { err = errorAt(path + ".itemId", "string", item); return {}; }
        if (!qty.isDouble()) { err = errorAt(path + ".qty", "number", qty); return {}; }
        s.itemId = item.toString();
        s.qty = int(qty.toDouble());
        return s;
    }
    if (t == "battle") {
        s.type = EventStep::Type::Battle;
        auto enc = o.value("encounterId");
        if (!enc.isString()) { err = errorAt(path + ".encounterId", "string", enc); return {}; }
        s.encounterId = enc.toString();
        return s;
    }
    if (t == "custom_note") {
        s.type = EventStep::Type::CustomNote;
        auto txt = o.value("text");
        if (!txt.isString()) { err = errorAt(path + ".text", "string", txt); return {}; }
        s.text = txt.toString();
        return s;
    }

    err.ok = false;
    err.error = QStringLiteral("Parse error at %1.type: unknown step '%2'").arg(path, t);
    return {};
}

ScenePlanParser::Result ScenePlanParser::parse(const QByteArray& jsonBytes)
{
    Err res; res.ok = false;
    QJsonParseError jerr;
    const auto doc = QJsonDocument::fromJson(jsonBytes, &jerr);
    if (jerr.error != QJsonParseError::NoError) {
        res.error = QStringLiteral("Invalid JSON: %1").arg(jerr.errorString());
        return res;
    }
    if (!doc.isObject()) {
        res.error = QStringLiteral("Root must be an object");
        return res;
    }
    const auto root = doc.object();

    // meta
    QJsonObject meta;
    if (!getObject(root, QStringLiteral("meta"), meta)) {
        return errorAt("meta", "object", root.value("meta"));
    }
    if (!meta.value("title").isString() || !meta.value("model").isString() || !meta.value("version").isString()) {
        return Err{false, {}, QStringLiteral("Parse error at meta: required fields title, model, version (strings)")};
    }
    ScenePlan out;
    out.meta.title = meta.value("title").toString();
    out.meta.model = meta.value("model").toString();
    out.meta.version = meta.value("version").toString();

    // actors (optional)
    if (root.contains("actors")) {
        const auto av = root.value("actors");
        if (!av.isArray()) return errorAt("actors", "array", av);
        const auto a = av.toArray();
        out.actors.reserve(a.size());
        for (int i=0;i<a.size();++i) {
            const QString base = QStringLiteral("actors[%1]").arg(i);
            const auto v = a.at(i);
            if (!v.isObject()) return errorAt(base, "object", v);
            const auto o = v.toObject();
            Actor actor;
            auto idv = o.value("id");
            if (!idv.isString()) return errorAt(base + ".id", "string", idv);
            actor.id = idv.toString();
            if (o.contains("displayName")) {
                auto dv = o.value("displayName");
                if (!dv.isString()) return errorAt(base + ".displayName", "string", dv);
                actor.displayName = dv.toString();
            }
            if (o.contains("pose")) {
                auto pv = o.value("pose");
                if (!pv.isString() || !isPose(pv.toString()))
                    return errorAt(base + ".pose", "\"idle\"|\"talk\"|\"walk\"", pv);
                actor.pose = pv.toString();
            }
            if (o.contains("position")) {
                Err e; actor.position = parsePoint(o.value("position"), base + ".position", e);
                if (!e.ok && !e.error.isEmpty()) return e;
            }
            if (o.contains("facing")) {
                auto fv = o.value("facing");
                if (!fv.isString() || !isFaceDir(fv.toString()))
                    return errorAt(base + ".facing", "\"N\"|\"S\"|\"E\"|\"W\"", fv);
                actor.facing = fv.toString().at(0);
            }
            out.actors.append(actor);
        }
    }

    // dialog (optional)
    if (root.contains("dialog")) {
        const auto dv = root.value("dialog");
        if (!dv.isArray()) return errorAt("dialog", "array", dv);
        const auto a = dv.toArray();
        out.dialog.reserve(a.size());
        for (int i=0;i<a.size();++i) {
            const QString base = QStringLiteral("dialog[%1]").arg(i);
            const auto v = a.at(i);
            if (!v.isObject()) return errorAt(base, "object", v);
            const auto o = v.toObject();
            const auto sid = o.value("speakerId");
            const auto txt = o.value("text");
            if (!sid.isString()) return errorAt(base + ".speakerId", "string", sid);
            if (!txt.isString()) return errorAt(base + ".text", "string", txt);
            out.dialog.append(DialogLine{ sid.toString(), txt.toString() });
        }
    }

    // events (optional)
    if (root.contains("events")) {
        const auto evv = root.value("events");
        if (!evv.isArray()) return errorAt("events", "array", evv);
        const auto a = evv.toArray();
        out.events.reserve(a.size());
        for (int i=0;i<a.size();++i) {
            const QString base = QStringLiteral("events[%1]").arg(i);
            const auto v = a.at(i);
            if (!v.isObject()) return errorAt(base, "object", v);
            const auto o = v.toObject();
            EventDef e;
            auto idv = o.value("id");
            if (!idv.isString()) return errorAt(base + ".id", "string", idv);
            e.id = idv.toString();
            auto trg = o.value("trigger");
            if (!trg.isString() || !isTrigger(trg.toString()))
                return errorAt(base + ".trigger", "\"on_enter\"|\"on_interact\"|\"auto\"|\"zone\"", trg);
            e.trigger = trg.toString();
            if (o.contains("triggerZone")) {
                Err e2; e.triggerZone = parseRect(o.value("triggerZone"), base + ".triggerZone", e2);
                if (!e2.ok && !e2.error.isEmpty()) return e2;
            }
            auto stepsV = o.value("steps");
            if (!stepsV.isArray()) return errorAt(base + ".steps", "array", stepsV);
            const auto arr = stepsV.toArray();
            for (int j=0;j<arr.size();++j) {
                EventStep s = parseStep(arr.at(j), QString("%1.steps[%2]").arg(base).arg(j), res);
                if (!res.ok && !res.error.isEmpty()) return res;
                e.steps.append(s);
            }
            out.events.append(e);
        }
    }

    // layout (optional)
    if (root.contains("layout")) {
        const auto lv = root.value("layout");
        if (!lv.isObject()) return errorAt("layout", "object", lv);
        const auto lo = lv.toObject();
        // props
        if (lo.contains("props")) {
            auto pv = lo.value("props");
            if (!pv.isArray()) return errorAt("layout.props", "array", pv);
            const auto arr = pv.toArray();
            for (int i=0;i<arr.size();++i) {
                const QString base = QStringLiteral("layout.props[%1]").arg(i);
                auto it = arr.at(i);
                if (!it.isObject()) return errorAt(base, "object", it);
                const auto po = it.toObject();
                LayoutProp p;
                auto idv = po.value("id");
                if (!idv.isString()) return errorAt(base + ".id", "string", idv);
                p.id = idv.toString();
                Err e; p.position = parsePoint(po.value("position"), base + ".position", e);
                if (!e.ok && !e.error.isEmpty()) return e;
                out.layout.props.append(p);
            }
        }
        if (lo.contains("spawnPoint")) {
            Err e; out.layout.spawnPoint = parsePoint(lo.value("spawnPoint"), "layout.spawnPoint", e);
            if (!e.ok && !e.error.isEmpty()) return e;
        }
        if (lo.contains("restrictions")) {
            const auto r = lo.value("restrictions");
            if (!r.isObject()) return errorAt("layout.restrictions", "object", r);
            const auto ro = r.toObject();
            if (ro.contains("walkmeshHint")) {
                auto w = ro.value("walkmeshHint");
                if (!w.isString()) return errorAt("layout.restrictions.walkmeshHint", "string", w);
                out.layout.walkmeshHint = w.toString();
            }
        }
    }

    res.ok = true;
    res.plan = std::move(out);
    return res;
}

