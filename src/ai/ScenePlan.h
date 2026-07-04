#pragma once

#include <QtCore>

// Scene model produced by parser and consumed by mapper

struct Actor {
    QString id;
    QString displayName;
    QString pose;              // "idle" | "talk" | "walk" (optional)
    QPoint position{0,0};      // 2D field-local coords
    QChar facing{};            // 'N','S','E','W' (optional)
};

struct DialogLine {
    QString speakerId;         // must match Actor.id or "narrator"
    QString text;
};

struct EventStep {
    enum class Type {
        Say,
        Move,
        Face,
        Wait,
        PlayMusic,
        SetFlag,
        IfFlag,
        GiveItem,
        Battle,
        CustomNote
    } type{Type::CustomNote};

    // Common
    QString actorId;

    // Say
    QString text;

    // Move
    QPoint to{0,0};
    double speed{0.0};

    // Face
    QChar dir{};

    // Wait
    int ms{0};

    // PlayMusic
    QString track;

    // SetFlag / IfFlag
    QString key;
    bool value{false};
    QVector<EventStep> thenSteps;
    QVector<EventStep> elseSteps;

    // GiveItem
    QString itemId;
    int qty{0};

    // Battle
    QString encounterId;
};

struct EventDef {
    QString id;
    QString trigger;           // "on_enter" | "on_interact" | "auto" | "zone"
    QRect triggerZone{0,0,0,0};
    QVector<EventStep> steps;
};

struct LayoutProp {
    QString id;
    QPoint position{0,0};
};

struct LayoutDef {
    QVector<LayoutProp> props;
    QPoint spawnPoint{0,0};
    QString walkmeshHint;      // "open" | "tight"
};

struct ScenePlanMeta {
    QString title;
    QString model;
    QString version;
};

struct ScenePlan {
    ScenePlanMeta meta;
    QVector<Actor> actors;
    QVector<DialogLine> dialog;
    QVector<EventDef> events;
    LayoutDef layout;
};

