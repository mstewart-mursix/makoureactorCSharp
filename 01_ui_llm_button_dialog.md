Role



Add one toolbar button and a Qt dialog to capture the user’s story prompt and settings. Wire it so other modules can call back with results.



Inputs



Active field/map context inside Makou Reactor.



Feature flag MR\_ENABLE\_LLM\_GENERATOR.



Outputs



LLMSceneDialog (src/ui/LLMSceneDialog.h/.cpp).



Signal/slot API: requestGenerate(ScenePrompt prompt), showPreview(ScenePlan plan), showError(QString).



Steps



Button



Add “LLM Generate” to the field editor toolbar (disabled when no field open).



Action id: ActionId::GenerateSceneViaLLM.



Dialog



Fields:



Multiline Prompt (characters, motivations, setting).



Optional controls (checkboxes): “Generate Dialog”, “Generate Map Layout”, “Generate Scripts”.



Temperature (0–1), Max Tokens, Model name (string, default from settings).



Endpoint URL (read-only if set globally).



Buttons: Generate, Cancel.



Preview Panel



After generation: tabbed preview:



Dialog (per character, per line).



Events/Triggers list.



Actors \& positions.



Script snippet view.



Buttons: Apply, Discard.



API



LLMSceneDialog::setResult(ScenePlan plan)



LLMSceneDialog::resultAccepted() signal when user clicks Apply.



Minimal Scaffold (C++)

// src/ui/LLMSceneDialog.h

\#pragma once

\#include <QDialog>

\#include <memory>



struct ScenePrompt {

&nbsp; QString userText;

&nbsp; bool genDialog{true};

&nbsp; bool genLayout{true};

&nbsp; bool genScripts{true};

&nbsp; double temperature{0.6};

&nbsp; int maxTokens{2048};

&nbsp; QString modelName;

};



struct ScenePlan; // forward-declared; defined by parser module



class LLMSceneDialog : public QDialog {

&nbsp; Q\_OBJECT

public:

&nbsp; explicit LLMSceneDialog(QWidget\* parent=nullptr);

&nbsp; ScenePrompt prompt() const;

&nbsp; void setResult(const ScenePlan\& plan);

signals:

&nbsp; void requestGenerate(const ScenePrompt\& prompt);

&nbsp; void applyRequested(const ScenePlan\& plan);

&nbsp; void discarded();

public slots:

&nbsp; void showError(const QString\& msg);

};



Acceptance Criteria



Button is visible \& disabled when no field is open.



Dialog collects prompt; emits requestGenerate.



Preview displays plan; Apply emits applyRequested.

