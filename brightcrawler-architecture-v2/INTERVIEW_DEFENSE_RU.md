# Защита архитектуры — тезисы

## Одно предложение

> Я выбрал pragmatic modular monolith, где PostgreSQL выступает как durable crawl frontier: он хранит identity URL, eligibility, leases, retries и terminal history, а workers напрямую атомарно получают работу через `FOR UPDATE SKIP LOCKED`.

## Что такое frontier

Frontier — не просто FIFO-очередь. Это scheduler crawler-а, который знает:

- какие canonical URL обнаружены;
- какие можно брать сейчас;
- какие leased worker-ам;
- какие отложены до `available_at`;
- какой URL взять следующим;
- закончился ли crawl.

Активный frontier — `Pending`, `Leased`, `RetryScheduled`. Завершённые записи остаются рядом как crawl ledger для дедупликации, resume и диагностики.

## Почему `ICrawlFrontier`, а не repository

`Repository` обычно предлагает CRUD и позволяет произвольные изменения. Здесь нужны доменные атомарные операции:

```text
CreateRun
TryLeaseNext
CompleteSuccess
CompleteRedirect
ScheduleRetry
CompleteTerminal
GetSnapshot
```

Так нельзя обойти lease token или случайно выполнить недопустимый transition.

## Какие гарантии даёт система

Не надо говорить «exactly once fetch».

Правильно:

- один canonical URL регистрируется один раз на crawl run;
- одновременно действителен только один lease;
- завершить запись может только владелец текущего lease token;
- transient failure или crash могут привести к повторному fetch;
- повторное выполнение безопасно благодаря idempotent completion и content-addressed storage.

Внешний HTTP-запрос и транзакцию PostgreSQL невозможно commit-ить атомарно. Поэтому безопасный replay честнее и корректнее, чем обещание exactly-once.

## Почему PostgreSQL, а не RabbitMQ

PostgreSQL уже даёт всё, что нужно текущей системе:

```text
durable state
unique canonical URL
atomic claim
retry schedule
leases
resume
attempt history
inspectability
```

RabbitMQ добавил бы второй источник состояния — broker и database. Понадобились бы outbox/inbox, idempotent consumer и reconciliation. На текущем масштабе новой необходимой возможности это не создаёт.

Broker станет оправдан, когда fetch и processing должны независимо масштабироваться, появятся большие распределённые кластеры или отдельные pipeline stages.

## Почему нет `Channel<T>`

`Channel` создал бы вторую недолговечную frontier в памяти. URL мог бы быть leased в БД, но ещё ждать в channel. При crash понадобились бы отдельные правила prefetch и recovery. Workers проще и надёжнее claim-ят PostgreSQL напрямую.

## Почему retries не в Polly

Retry — часть состояния crawler-а, а не скрытая транспортная деталь. Важно видеть:

```text
attempt number
status code
error
Retry-After
next available time
final outcome
```

Поэтому retry сохраняется в PostgreSQL и выполняется новым frontier transition. Невидимые HTTP retries испортили бы observability и attempt accounting.

## Что происходит на `429`

- парсим `Retry-After` как seconds или HTTP-date;
- вычисляем not-before не раньше server hint;
- переводим URL в `RetryScheduled`;
- сохраняем `available_at`;
- обновляем общий process-wide cooldown, чтобы другие workers не продолжали давить на тот же Fetch API.

## Почему exact-host scope

Это простая и безопасная политика для take-home. Она не требует частично и потенциально неверно реализовывать Public Suffix List. Subdomains/allowlist можно добавить отдельной policy без изменения worker pipeline.

## Почему content hash в имени

```text
output/images/ab/<sha256>.jpg
```

Это решает collisions, query-string filenames, path traversal, дедуп одинаковых bytes и change detection. Связь URL → artifact хранится в БД.

## Что будет особенно хорошо смотреться в коде

- `ICrawlFrontier` с use-case методами;
- lease token как fencing token;
- explicit SQL claim и integration test на PostgreSQL;
- atomic HTML completion + insert discoveries;
- `Retry-After` без hidden retry;
- MIME dispatch при обманчивом extension;
- тест `A → B → C → A`;
- таблица deliberate omissions в README.

## Production evolution

Сначала:

```text
one process + PostgreSQL + filesystem
```

Потом:

```text
multiple workers + object storage + OpenTelemetry
```

На большом масштабе:

```text
partitioned frontier
scheduler/outbox/broker
separate fetch and processing pools
distributed rate state
compliance policy
```

Важно: broker может стать транспортом, но frontier остаётся источником scheduling state.
