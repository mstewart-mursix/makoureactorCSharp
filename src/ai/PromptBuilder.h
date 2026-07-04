#pragma once

#include <QtCore>

#include "ui/LLMSceneDialog.h" // for ScenePrompt
#include "LLMSchemas.h"
#include "core/field/Field.h"

namespace LLM {

// Returns the system prompt per spec
QString systemPrompt();

// Returns the schema text to embed in the prompt
QString schemaText();

// Builds the user prompt using the ScenePrompt and field size hints
QString userPrompt(const ScenePrompt& p, int widthPx, int heightPx);

// Convenience: compose a complete LLMRequest
LLMRequest buildRequestFrom(const ScenePrompt& p,
                            const QString& endpoint,
                            const QString& model,
                            int widthPx,
                            int heightPx);

}

