# Техническая детализация задач (Execution Plan)

Источник: `stories-plan.md` + `tasks-plan.md`  
Цель: превратить backlog в задачи уровня “берем в работу и реализуем”.

## 0. Общие инженерные правила
- Кодстайл: FE `eslint + prettier`, BE `dotnet format`.
- Тесты:
  - FE: `vitest` + `@testing-library/react`.
  - BE: `xUnit` + интеграционные тесты на test PostgreSQL.
- Контракты API: OpenAPI обязателен, ошибки в формате ProblemDetails.
- Миграции БД: только через EF Core migrations, без ручных SQL в обход.
- Все эндпоинты домена карт/маршрутов обязаны фильтровать по `OwnerUserId`.

## Wave 1: Foundation + Auth + Maps

### TASK-0001..0008 Foundation
1. Создать структуру:
   - `frontend/`, `backend/`, `infra/`, `docs/`, `.github/workflows/`.
2. FE bootstrap:
   - Vite + React + TS.
   - React Router.
   - Базовый layout (shell + route outlet).
3. BE bootstrap:
   - ASP.NET Core Web API.
   - Swagger/OpenAPI.
   - Health endpoint `/health`.
4. Инфраструктура БД:
   - Подключить PostgreSQL.
   - Добавить `DbContext` + миграции.
5. Cross-cutting:
   - Middleware `Correlation-Id`.
   - ProblemDetails handler.
   - Structured logging (Serilog/аналог).
6. CI:
   - FE: install, lint, test, build.
   - BE: restore, build, test.
   - Проверка миграций на чистой БД.

Definition of Done:
- Локально стартуют FE+BE.
- CI green.
- Документация запуска в `README`.

### TASK-1001..1009 Auth
1. Модель и БД:
   - Таблица `Users` (Email unique, PasswordHash, Role, CreatedAt).
   - Таблица/хранилище refresh-токенов.
2. API:
   - `POST /auth/register`
   - `POST /auth/login`
   - `POST /auth/refresh`
   - `POST /auth/forgot-password`
   - `POST /auth/reset-password`
3. Безопасность:
   - Хэш пароля (`Argon2`/`BCrypt`).
   - JWT (short-lived access + refresh rotation).
   - Rate limit на auth endpoints.
4. FE:
   - Страницы login/register/reset.
   - Auth context/store.
   - Axios/fetch interceptor для refresh.
5. RBAC + tenancy:
   - Роли `User/Admin`.
   - Глобальный guard на доступ к “чужим” сущностям.

Тесты:
- Unit: password hashing, token generation/validation.
- Integration: register/login/refresh/reset.
- FE: auth form validation + redirect flows.

### TASK-2001..2008 Maps
1. Storage adapter:
   - Интерфейс `IFileStorage` (`Save`, `Get`, `Delete`).
   - Реализация LocalStorage (MVP), подготовка S3 adapter.
2. API карт:
   - `POST /maps/upload`
   - `GET /maps`
   - `GET /maps/{id}`
   - `GET /maps/{id}/versions`
3. Валидация upload:
   - MIME whitelist: image/png, image/jpeg.
   - Size limit (конфиг).
   - Безопасные имена и path traversal защита.
4. Версионирование:
   - `Map` + `MapVersion` (active version pointer).
   - Статусы: Uploaded/Digitized/Edited/Ready.
5. FE:
   - Список карт, карточка карты, статусы.
   - Upload UI + progress + error state.
   - Просмотр версий карты.

Тесты:
- Integration: upload + retrieval + version listing + tenant isolation.
- FE: upload happy path + invalid type/size.

## Wave 2: Digitization + Editor + Terrain Types

### TASK-3001..3007 Digitization
1. Job модель:
   - Таблица `DigitizationJob` (Status, Progress, Error, StartedAt, FinishedAt).
   - Статусы: queued/running/completed/failed.
2. CV pipeline (MVP baseline):
   - Предобработка изображения.
   - Цветовые маски по классам.
   - Морфология + контуры.
   - Преобразование в геометрии точка/линия/полигон.
3. Домен:
   - `TerrainObject` поля: class/type/geometry/traversability/source.
   - source = `auto` для результатов pipeline.
4. API:
   - `POST /maps/{id}/digitize` (start).
   - `GET /maps/{id}/digitize/{jobId}` (status/progress).
5. FE:
   - Кнопка запуска + polling прогресса.
   - Отображение состояния и ошибок.
6. Качество:
   - Сервис расчета Macro F1, IoU.
   - Отчет по тестовому набору карт.

### TASK-4001..4009 Editor (react-konva)
1. Canvas ядро:
   - Stage/Layer abstraction.
   - Координатная система изображения.
   - Zoom/pan.
2. Редактирование геометрии:
   - Point/Polyline/Polygon tools.
   - Vertex drag/edit.
   - Select/delete/update class/type.
3. Слои:
   - Source / Digitized / Graph / Route toggles.
4. Undo/Redo:
   - Command stack (операции create/update/delete).
5. Persist:
   - `PUT /maps/{id}/objects`.
   - Сохранение в новую `MapVersion`.

Тесты:
- FE unit: geometry reducers/commands.
- FE integration: draw/edit/delete/save.
- BE integration: objects update creates new version.

### TASK-5001..5004 Terrain Types
1. Seed системных типов и коэффициентов.
2. CRUD API `terrain-types` с tenant isolation.
3. FE справочник типов + интеграция в редактор.

Тесты:
- Integration: CRUD + isolation.
- FE: create/edit/delete type flow.

## Wave 3: Routing + Explainability

### TASK-6001..6010 Routing
1. Graph builder:
   - Генерация `GraphNode`, `GraphEdge` из оцифровки.
   - Непроходимые зоны исключить.
   - Вес = длина * коэффициенты покрытия + penalties.
2. Алгоритмы:
   - A* для single route.
   - Top-k (3) с diversity-ограничением (overlap threshold).
3. Домен маршрута:
   - `RouteRequest`, `RouteResult`, `RouteSegment`.
   - Метрики: estimatedTime, riskScore, penaltyScore, totalCost.
4. API:
   - `POST /routes/calculate`
   - Опционально async mode + progress endpoint.
5. FE:
   - Выбор точек на карте.
   - Карточки сравнения top-3.
   - Progress bar для расчета.

Тесты:
- Unit: cost function, overlap metric.
- Integration: A*, top-3, diversity filtering.
- Perf test: целевой SLA <10s на типовом датасете.

### TASK-7001..7003 Explainability
1. Back-end payload:
   - `whyChosen[]`, breakdown по факторам (time/risk/penalty).
2. FE блок:
   - “Почему этот маршрут”
   - Сравнение отличий между вариантами.
3. Визуал:
   - Цветовая шкала риска на сегментах.
   - Легенда.

Тесты:
- Snapshot/contract тест формата explainability.
- FE visual regression на карточках маршрутов.

## Wave 4: History + Export + Hardening

### TASK-8001..8004 History
1. Persist всех запусков маршрутизации с привязкой к версии карты.
2. API истории:
   - `GET /maps/{id}/routes/history`
3. Re-run:
   - Endpoint/flow повторного расчета из исторических параметров.
4. FE:
   - Экран истории + действие “повторить”.

### TASK-9001..9004 Export
1. Export service:
   - `POST /routes/{id}/export?format=png|pdf`
2. Генерация PNG:
   - Карта + трек + точки + легенда.
3. Генерация PDF:
   - Печатный макет + summary маршрута.
4. FE:
   - Кнопки экспорта, статус и скачивание.

Тесты:
- Integration: export job success/failure.
- Snapshot проверки контента экспорта.

### TASK-10001..10003 UX
1. Основной экран:
   - Центр карта, справа control/results panel.
2. Тема:
   - Design tokens для dark + red-orange accents.
3. State UX:
   - empty/loading/error/success на всех ключевых сценариях.

### TASK-11001..11005 Security/Operations
1. Upload hardening:
   - MIME/ext/size, запрет опасных файлов.
2. Backup/restore:
   - Daily backup job + restore playbook.
3. Observability:
   - Метрики latency/error/uptime.
   - Dashboard и алерты.
4. Бизнес-метрики:
   - время digitize/route/export, доля ошибок.

## Wave 5: QA и приемка релиза

### TASK-12001..12005 Testing & Acceptance
1. Unit + integration tests routing engine.
2. E2E сценарий:
   - upload -> digitize -> edit -> route -> history -> export.
3. Acceptance checklist:
   - Полное соответствие `prd.md` критериям MVP.
4. Release readiness report:
   - SLA/качество/регрессии/известные ограничения.

## Что нужно подготовить перед стартом кодинга
1. Выбрать FE state-management (`Redux Toolkit` или `Zustand`) и зафиксировать.
2. Зафиксировать контракт геометрий (`GeoJSON-like` DTO) между FE и BE.
3. Зафиксировать порог diversity (например, overlap > 70% = “похожий”).
4. Определить лимиты upload (размер/разрешение/кол-во объектов).
5. Подготовить тестовый набор карт для performance и качества оцифровки.

## Команда для реализации по порядку (рекомендуемая)
1. Foundation/Auth/Maps.
2. Digitization + Editor + Terrain Types.
3. Routing + Explainability.
4. History + Export + UX polish.
5. Security/Ops + QA + Acceptance.
