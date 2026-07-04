#pragma once

#include <QtCore>

// Request DTO for LM Studio OpenAI-style chat API
struct LLMRequest {
    QString endpointUrl;            // e.g. http://127.0.0.1:1234/v1/chat/completions
    QString model;                  // model name
    QString apiKey;                 // optional API key (e.g., lm-studio)
    double temperature{0.6};
    int maxTokens{2048};
    QString systemPrompt;           // optional system instructions
    QString userPrompt;             // main user content
    bool stream{false};
    int timeoutMs{30000};
};

// Raw result: HTTP + parsed JSON if available
struct LLMRawResult {
    bool ok{false};
    int httpStatus{0};
    QString error;                  // human-friendly error
    QByteArray raw;                 // raw response body
    QJsonDocument json;             // parsed JSON when possible
};
