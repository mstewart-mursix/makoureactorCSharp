#include "LLMConfig.h"
#include "core/Config.h"

static QStringList candidatePaths()
{
    QStringList paths;
    // 1) Explicit path via QSettings
    const QString fromSettings = Config::value(QStringLiteral("LLM_CONFIG_PATH")).toString();
    if (!fromSettings.isEmpty()) paths << fromSettings;
    // 2) Working directory
    paths << QDir::current().filePath(QStringLiteral("llm.config.json"));
    // 3) Application directory
    paths << QCoreApplication::applicationDirPath() + QLatin1String("/llm.config.json");
    // 4) Program resource dir
    paths << Config::programResourceDir() + QLatin1String("/llm.config.json");
    return paths;
}

LLMConfig LLMConfig::load(const QString& overridePath)
{
    LLMConfig cfg;
    QString filePath = overridePath;
    if (filePath.isEmpty()) {
        for (const QString& p : candidatePaths()) {
            if (QFileInfo::exists(p)) { filePath = p; break; }
        }
    }
    if (filePath.isEmpty()) {
        // Defaults if file not found
        cfg.endpoint = QStringLiteral("http://localhost:1234/v1/chat/completions");
        cfg.apiKey = QStringLiteral("lm-studio");
        cfg.model = QStringLiteral("qwen/qwen3-coder-30b");
        return cfg;
    }

    QFile f(filePath);
    if (!f.open(QIODevice::ReadOnly)) {
        // Fallback to sensible defaults
        cfg.endpoint = QStringLiteral("http://localhost:1234/v1/chat/completions");
        cfg.apiKey = QStringLiteral("lm-studio");
        cfg.model = QStringLiteral("qwen/qwen3-coder-30b");
        return cfg;
    }
    const QByteArray data = f.readAll();
    f.close();

    QJsonParseError jerr;
    const auto doc = QJsonDocument::fromJson(data, &jerr);
    if (jerr.error != QJsonParseError::NoError || !doc.isObject()) {
        cfg.endpoint = QStringLiteral("http://localhost:1234/v1/chat/completions");
        cfg.apiKey = QStringLiteral("lm-studio");
        cfg.model = QStringLiteral("qwen/qwen3-coder-30b");
        return cfg;
    }
    const QJsonObject o = doc.object();
    cfg.endpoint = o.value(QStringLiteral("endpoint")).toString(cfg.endpoint);
    cfg.apiKey  = o.value(QStringLiteral("api_key")).toString(cfg.apiKey);
    cfg.model   = o.value(QStringLiteral("model")).toString(cfg.model);
    // Ensure endpoint path points at chat completions
    if (cfg.endpoint.endsWith(QLatin1String("/v1"))) {
        cfg.endpoint += QLatin1String("/chat/completions");
    }
    return cfg;
}

