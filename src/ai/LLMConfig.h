#pragma once

#include <QtCore>

// Simple JSON-based config for LLM settings.
// Looks for llm.config.json in working dir, application dir, or resource dir.
struct LLMConfig {
    QString endpoint;   // full URL to chat completions endpoint
    QString apiKey;     // e.g., "lm-studio"
    QString model;      // e.g., "qwen/qwen3-coder-30b"

    static LLMConfig load(const QString& overridePath = QString());
};

