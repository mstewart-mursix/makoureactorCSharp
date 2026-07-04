#include "LLMSceneDialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QTabWidget>
#include <QTextEdit>
#include <QLineEdit>
#include <QDoubleSpinBox>
#include <QSpinBox>
#include <QCheckBox>
#include <QPushButton>
#include <QLabel>
#include <QMessageBox>

LLMSceneDialog::LLMSceneDialog(QWidget* parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Generate Scene via LLM"));
    resize(720, 540);
    buildUi();
    showInputPanel();
}

void LLMSceneDialog::buildUi()
{
    auto* root = new QVBoxLayout(this);

    // Input panel
    auto* input = new QWidget(this);
    auto* grid = new QGridLayout(input);

    promptEdit = new QTextEdit(input);
    promptEdit->setPlaceholderText(tr("Describe the scene: characters, motivations, setting..."));
    promptEdit->setAcceptRichText(false);

    cbDialog = new QCheckBox(tr("Generate Dialog"), input);
    cbDialog->setChecked(true);
    cbLayout = new QCheckBox(tr("Generate Map Layout"), input);
    cbLayout->setChecked(true);
    cbScripts = new QCheckBox(tr("Generate Scripts"), input);
    cbScripts->setChecked(true);

    sbTemperature = new QDoubleSpinBox(input);
    sbTemperature->setRange(0.0, 1.0);
    sbTemperature->setSingleStep(0.05);
    sbTemperature->setValue(0.6);

    sbMaxTokens = new QSpinBox(input);
    sbMaxTokens->setRange(128, 32768);
    sbMaxTokens->setValue(2048);

    leModel = new QLineEdit(input);
    leModel->setPlaceholderText(tr("Model name (e.g. mistral, llama3)"));

    leEndpoint = new QLineEdit(input);
    leEndpoint->setPlaceholderText(tr("Endpoint URL (e.g. http://localhost:1234/v1/chat/completions)"));

    int row = 0;
    grid->addWidget(new QLabel(tr("Prompt"), input), row, 0, 1, 1);
    grid->addWidget(promptEdit, row, 1, 1, 3); row++;
    grid->addWidget(cbDialog, row, 1);
    grid->addWidget(cbLayout, row, 2);
    grid->addWidget(cbScripts, row, 3); row++;
    grid->addWidget(new QLabel(tr("Temperature"), input), row, 0);
    grid->addWidget(sbTemperature, row, 1);
    grid->addWidget(new QLabel(tr("Max Tokens"), input), row, 2);
    grid->addWidget(sbMaxTokens, row, 3); row++;
    grid->addWidget(new QLabel(tr("Model"), input), row, 0);
    grid->addWidget(leModel, row, 1, 1, 3); row++;
    grid->addWidget(new QLabel(tr("Endpoint URL"), input), row, 0);
    grid->addWidget(leEndpoint, row, 1, 1, 3); row++;

    auto* inputButtons = new QHBoxLayout();
    btnGenerate = new QPushButton(tr("Generate"), input);
    btnCancel = new QPushButton(tr("Cancel"), input);
    inputButtons->addStretch();
    inputButtons->addWidget(btnGenerate);
    inputButtons->addWidget(btnCancel);

    auto* inputWrap = new QVBoxLayout();
    inputWrap->addLayout(grid);
    inputWrap->addLayout(inputButtons);
    input->setLayout(inputWrap);

    // Preview panel
    previewPanel = new QWidget(this);
    auto* previewLayout = new QVBoxLayout(previewPanel);
    previewTabs = new QTabWidget(previewPanel);
    placeholderDialog = new QLabel(tr("Dialog preview will appear here."), previewTabs);
    placeholderDialog->setWordWrap(true);
    placeholderEvents = new QLabel(tr("Events and triggers preview will appear here."), previewTabs);
    placeholderEvents->setWordWrap(true);
    placeholderActors = new QLabel(tr("Actors and positions preview will appear here."), previewTabs);
    placeholderActors->setWordWrap(true);
    placeholderScripts = new QLabel(tr("Script snippet preview will appear here."), previewTabs);
    placeholderScripts->setWordWrap(true);
    previewTabs->addTab(placeholderDialog, tr("Dialog"));
    previewTabs->addTab(placeholderEvents, tr("Events/Triggers"));
    previewTabs->addTab(placeholderActors, tr("Actors"));
    previewTabs->addTab(placeholderScripts, tr("Scripts"));

    auto* previewButtons = new QHBoxLayout();
    btnApply = new QPushButton(tr("Apply"), previewPanel);
    btnDiscard = new QPushButton(tr("Discard"), previewPanel);
    previewButtons->addStretch();
    previewButtons->addWidget(btnApply);
    previewButtons->addWidget(btnDiscard);

    previewLayout->addWidget(previewTabs);
    previewLayout->addLayout(previewButtons);

    root->addWidget(input);
    root->addWidget(previewPanel);

    // Connections
    connect(btnCancel, &QPushButton::clicked, this, &QDialog::reject);
    connect(btnGenerate, &QPushButton::clicked, this, [this]() {
        emit requestGenerate(this->prompt());
    });
    connect(btnApply, &QPushButton::clicked, this, [this]() {
        if (currentPlan) {
            emit applyRequested(*currentPlan);
        } else {
            QMessageBox::warning(this, tr("No Result"), tr("Nothing to apply yet."));
        }
    });
    connect(btnDiscard, &QPushButton::clicked, this, [this]() {
        emit discarded();
        showInputPanel();
    });
}

ScenePrompt LLMSceneDialog::prompt() const
{
    ScenePrompt p;
    p.userText = promptEdit->toPlainText();
    p.genDialog = cbDialog->isChecked();
    p.genLayout = cbLayout->isChecked();
    p.genScripts = cbScripts->isChecked();
    p.temperature = sbTemperature->value();
    p.maxTokens = sbMaxTokens->value();
    p.modelName = leModel->text();
    p.endpointUrl = leEndpoint->text();
    return p;
}

void LLMSceneDialog::setResult(const ScenePlan& plan)
{
    // Store a deep copy to keep lifetime safe
    currentPlan = std::make_unique<ScenePlan>(plan);
    showPreviewPanel();
}

void LLMSceneDialog::showError(const QString& msg)
{
    QMessageBox::critical(this, tr("Generation Error"), msg);
}

void LLMSceneDialog::showInputPanel()
{
    if (previewPanel) previewPanel->hide();
    // Show only input fields and Generate/Cancel buttons
    if (btnGenerate) btnGenerate->setEnabled(true);
    if (btnApply) btnApply->setEnabled(true);
    if (btnDiscard) btnDiscard->setEnabled(true);
}

void LLMSceneDialog::showPreviewPanel()
{
    if (previewPanel) previewPanel->show();
}

void LLMSceneDialog::setRawPreview(const QString& jsonText)
{
    if (!previewPanel) return;
    placeholderDialog->setText(jsonText);
    placeholderEvents->setText(jsonText);
    placeholderActors->setText(jsonText);
    placeholderScripts->setText(jsonText);
    showPreviewPanel();
}

void LLMSceneDialog::setApplyEnabled(bool enabled)
{
    if (btnApply) btnApply->setEnabled(enabled);
}
