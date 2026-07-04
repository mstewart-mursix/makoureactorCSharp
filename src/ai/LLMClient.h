#pragma once

#include <QObject>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QFuture>

#include "LLMSchemas.h"

class LLMClient : public QObject {
    Q_OBJECT
public:
    explicit LLMClient(QObject* parent=nullptr);

    QFuture<LLMRawResult> requestScenePlan(const LLMRequest& req);
    void cancelAll();

private:
    QNetworkAccessManager nam_;
    QList<QPointer<QNetworkReply>> inFlight_;
};

