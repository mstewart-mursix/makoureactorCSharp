#pragma once

#include <QDialog>
#include <QPointer>

#include <memory>

#include "ai/ScenePlan.h"

struct ScenePrompt {
    QString userText;
    bool genDialog{true};
    bool genLayout{true};
    bool genScripts{true};
    double temperature{0.6};
    int maxTokens{2048};
    QString modelName;
    QString endpointUrl; // optional; may be read-only when set globally
};

// ScenePlan is defined in ai/ScenePlan.h

class QTabWidget;
class QTextEdit;
class QLineEdit;
class QDoubleSpinBox;
class QSpinBox;
class QCheckBox;
class QPushButton;
class QLabel;

class LLMSceneDialog : public QDialog {
    Q_OBJECT
public:
    explicit LLMSceneDialog(QWidget* parent=nullptr);

    ScenePrompt prompt() const;
    void setResult(const ScenePlan& plan);
    // Temporary helper for step 02: show raw JSON preview before parser is ready
    void setRawPreview(const QString& jsonText);
    void setApplyEnabled(bool enabled);

signals:
    void requestGenerate(const ScenePrompt& prompt);
    void applyRequested(const ScenePlan& plan);
    void discarded();

public slots:
    void showError(const QString& msg);

private:
    // Input panel widgets
    QTextEdit* promptEdit{nullptr};
    QCheckBox* cbDialog{nullptr};
    QCheckBox* cbLayout{nullptr};
    QCheckBox* cbScripts{nullptr};
    QDoubleSpinBox* sbTemperature{nullptr};
    QSpinBox* sbMaxTokens{nullptr};
    QLineEdit* leModel{nullptr};
    QLineEdit* leEndpoint{nullptr};
    QPushButton* btnGenerate{nullptr};
    QPushButton* btnCancel{nullptr};

    // Preview panel
    QWidget* previewPanel{nullptr};
    QTabWidget* previewTabs{nullptr};
    QLabel* placeholderDialog{nullptr};
    QLabel* placeholderEvents{nullptr};
    QLabel* placeholderActors{nullptr};
    QLabel* placeholderScripts{nullptr};
    QPushButton* btnApply{nullptr};
    QPushButton* btnDiscard{nullptr};

    // Stores the latest plan reference (opaque)
    std::unique_ptr<ScenePlan> currentPlan;

    void buildUi();
    void showInputPanel();
    void showPreviewPanel();
};
