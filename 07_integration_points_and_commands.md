Role



Wire everything into Makou Reactor without destabilizing existing code.



Inputs



Modules from 01–06.



Outputs



LLMSceneController that coordinates UI → LLM client → parser → validation → preview → apply.



Steps



Controller (src/ai/LLMSceneController.\*)



Connect LLMSceneDialog::requestGenerate → build LLMRequest → LLMClient.



On response → parse → validate → dialog.setResult(plan) or showError.



Apply Flow



On applyRequested(plan) → call mapper to produce field mutations.



Wrap in undo group: “LLM Scene Apply”.



Refresh field view.



Settings



Add LLM\_ENDPOINT\_URL, LLM\_MODEL, MR\_ENABLE\_LLM\_GENERATOR to settings UI or ini.



Acceptance Criteria



All interactions go through LLMSceneController.



Feature is off if flag is false.

