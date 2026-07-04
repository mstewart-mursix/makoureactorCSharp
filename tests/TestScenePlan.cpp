#include <QtTest/QtTest>
#include <QFile>
#include <QFileInfo>

#include "ai/ScenePlanParser.h"
#include "ai/ScenePlanValidator.h"
#include "ai/LayoutGenerator.h"
#include "ai/ScenePlanMapper.h"

static QString fixturePath(const QString& name)
{
    // Resolve relative to this test source file directory
    QFileInfo fi(QString::fromUtf8(__FILE__));
    QDir dir = fi.dir(); // tests/
    dir.cd("fixtures");
    return dir.absoluteFilePath(name);
}

class TestScenePlan : public QObject {
    Q_OBJECT
private slots:
    void parser_valid_fixture();
    void parser_wrong_type_ms();
    void validator_missing_actor();
    void validator_duplicate_actor();
    void validator_long_lines();
    void validator_bounds_warning();
    void layout_overlap_resolution();
    void smoke_preview_mapper();
};

void TestScenePlan::parser_valid_fixture()
{
    QFile f(fixturePath("plan_valid_01.json"));
    QVERIFY2(f.open(QIODevice::ReadOnly), "Cannot open fixture plan_valid_01.json");
    const auto res = ScenePlanParser::parse(f.readAll());
    QVERIFY2(res.ok, qPrintable(QString("Parser failed: %1").arg(res.error)));
    QCOMPARE(res.plan.meta.title, QStringLiteral("Test Scene"));
    QCOMPARE(res.plan.actors.size(), 2);
    QCOMPARE(res.plan.events.size(), 1);
}

void TestScenePlan::parser_wrong_type_ms()
{
    QFile f(fixturePath("plan_parser_wrong_type_ms.json"));
    QVERIFY2(f.open(QIODevice::ReadOnly), "Cannot open fixture plan_parser_wrong_type_ms.json");
    const auto res = ScenePlanParser::parse(f.readAll());
    QVERIFY(!res.ok);
    QVERIFY(res.error.contains("ms"));
}

void TestScenePlan::validator_missing_actor()
{
    QFile f(fixturePath("plan_invalid_missing_actor.json"));
    QVERIFY2(f.open(QIODevice::ReadOnly), "Cannot open fixture plan_invalid_missing_actor.json");
    const auto parsed = ScenePlanParser::parse(f.readAll());
    QVERIFY2(parsed.ok, qPrintable(parsed.error));
    const auto vres = LLMValidate::validate(parsed.plan, /*field*/nullptr, {});
    bool hasUnknown = false;
    for (const auto& i : vres.issues) {
        if (i.level == LLMValidate::Severity::Error && i.message.contains("Unknown")) {
            hasUnknown = true; break;
        }
    }
    QVERIFY(hasUnknown);
}

void TestScenePlan::validator_duplicate_actor()
{
    ScenePlan p; p.meta = {"Dup", "m", "1.0"};
    Actor a; a.id = "same"; a.position = QPoint(1,1);
    p.actors = { a, a };
    const auto vres = LLMValidate::validate(p, nullptr, {});
    bool hasDup = false;
    for (const auto& i : vres.issues) {
        if (i.level == LLMValidate::Severity::Error && i.message.contains("Duplicate actor")) { hasDup = true; break; }
    }
    QVERIFY(hasDup);
}

void TestScenePlan::validator_long_lines()
{
    ScenePlan p; p.meta = {"LongText", "m", "1.0"};
    Actor a; a.id = "cloud"; p.actors = { a };
    DialogLine d; d.speakerId = "cloud"; d.text = QString(120, 'x');
    p.dialog = { d };
    LLMValidate::Options o; o.maxLineLength = 100;
    const auto vres = LLMValidate::validate(p, nullptr, o);
    bool tooLong = false;
    for (const auto& i : vres.issues) if (i.level == LLMValidate::Severity::Error && i.message.contains("exceeds")) { tooLong = true; break; }
    QVERIFY(tooLong);
}

void TestScenePlan::validator_bounds_warning()
{
    ScenePlan p; p.meta = {"Bounds", "m", "1.0"};
    Actor a; a.id = "cloud"; a.position = QPoint(9999,9999); p.actors = { a };
    const auto vres = LLMValidate::validate(p, nullptr, {}); // nullptr -> 320x240 fallback bounds
    bool hasWarn = false;
    for (const auto& i : vres.issues) if (i.level == LLMValidate::Severity::Warn && i.path.contains("actors[0].position")) { hasWarn = true; break; }
    QVERIFY(hasWarn);
}

void TestScenePlan::layout_overlap_resolution()
{
    QFile f(fixturePath("plan_overlap_layout.json"));
    QVERIFY2(f.open(QIODevice::ReadOnly), "Cannot open fixture plan_overlap_layout.json");
    auto parsed = ScenePlanParser::parse(f.readAll());
    QVERIFY2(parsed.ok, qPrintable(parsed.error));
    LayoutOptions lo; lo.minDistancePx = 24; lo.nudgeStepPx = 8; lo.maxNudgeTries = 500;
    const auto r = LLMLayout::adjust(parsed.plan, nullptr, lo);
    // Ensure no pair of actors is closer than minDistance
    const auto& actors = parsed.plan.actors;
    for (int i=0;i<actors.size();++i) {
        for (int j=i+1;j<actors.size();++j) {
            const QPoint pi = actors[i].position;
            const QPoint pj = actors[j].position;
            const int dx = pi.x()-pj.x();
            const int dy = pi.y()-pj.y();
            const double d = std::sqrt(double(dx*dx + dy*dy));
            QVERIFY2(d >= lo.minDistancePx, "Actors overlap after layout adjustment");
        }
    }
}

void TestScenePlan::smoke_preview_mapper()
{
    QFile f(fixturePath("plan_valid_01.json"));
    QVERIFY2(f.open(QIODevice::ReadOnly), "Cannot open fixture plan_valid_01.json");
    auto parsed = ScenePlanParser::parse(f.readAll());
    QVERIFY2(parsed.ok, qPrintable(parsed.error));
    // Preview-only apply should not require a Field*
    auto result = ScenePlanMapper::applyToField(parsed.plan, /*field*/nullptr, ScenePlanMapper::ApplyOptions{true, {}});
    QVERIFY(result.ok);
    QVERIFY(!result.groupName.isEmpty());
    QVERIFY(!result.summary.isEmpty());
}

QTEST_MAIN(TestScenePlan)
#include "TestScenePlan.moc"
