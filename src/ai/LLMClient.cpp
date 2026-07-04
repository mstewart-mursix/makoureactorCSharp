#include "LLMClient.h"

#include <QNetworkRequest>
#include <QTimer>
#include <QJsonObject>
#include <QJsonArray>
#include <QJsonDocument>
#include <QPromise>

LLMClient::LLMClient(QObject* parent) : QObject(parent) {}

QFuture<LLMRawResult> LLMClient::requestScenePlan(const LLMRequest& req)
{
    QPromise<LLMRawResult> promise;
    auto future = promise.future();

    // Build payload for OpenAI-style chat completions
    QJsonObject root;
    root.insert(QStringLiteral("model"), req.model);
    root.insert(QStringLiteral("temperature"), req.temperature);
    root.insert(QStringLiteral("max_tokens"), req.maxTokens);
    root.insert(QStringLiteral("stream"), req.stream);

    QJsonArray messages;
    if (!req.systemPrompt.isEmpty()) {
        QJsonObject sys;
        sys.insert(QStringLiteral("role"), QStringLiteral("system"));
        sys.insert(QStringLiteral("content"), req.systemPrompt);
        messages.append(sys);
    }
    QJsonObject usr;
    usr.insert(QStringLiteral("role"), QStringLiteral("user"));
    usr.insert(QStringLiteral("content"), req.userPrompt);
    messages.append(usr);
    root.insert(QStringLiteral("messages"), messages);

    const QByteArray body = QJsonDocument(root).toJson(QJsonDocument::Compact);

    QNetworkRequest qreq(QUrl(req.endpointUrl));
    qreq.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    qreq.setRawHeader("Accept", "application/json");
    if (!req.apiKey.isEmpty()) {
        const QByteArray bearer = QByteArrayLiteral("Bearer ") + req.apiKey.toUtf8();
        qreq.setRawHeader("Authorization", bearer);
    }

    QNetworkReply* reply = nam_.post(qreq, body);
    inFlight_.append(reply);

    // Timeout handling
    QPointer<QTimer> timer = new QTimer(reply);
    timer->setSingleShot(true);
    timer->setInterval(req.timeoutMs > 0 ? req.timeoutMs : 30000);
    QObject::connect(timer, &QTimer::timeout, reply, [reply]() {
        if (reply && reply->isRunning()) {
            reply->abort();
        }
    });
    timer->start();

    // We need a heap-allocated promise to resolve later
    auto sp = std::make_shared<QPromise<LLMRawResult>>(std::move(promise));

    QObject::connect(reply, &QNetworkReply::finished, reply, [this, reply, sp, timer]() mutable {
        if (timer) timer->stop();
        inFlight_.removeAll(reply);

        LLMRawResult res;
        res.httpStatus = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        if (reply->error() != QNetworkReply::NoError) {
            res.ok = false;
            res.error = reply->errorString();
            res.raw = reply->readAll();
        } else {
            res.raw = reply->readAll();
            QJsonParseError jerr;
            res.json = QJsonDocument::fromJson(res.raw, &jerr);
            if (jerr.error != QJsonParseError::NoError) {
                res.ok = false;
                res.error = QStringLiteral("Invalid JSON: %1").arg(jerr.errorString());
            } else {
                res.ok = (res.httpStatus >= 200 && res.httpStatus < 300);
            }
        }

        sp->addResult(res);
        sp->finish();
        reply->deleteLater();
    });

    return future;
}

void LLMClient::cancelAll()
{
    for (auto& r : inFlight_) {
        if (r) r->abort();
    }
    inFlight_.clear();
}
