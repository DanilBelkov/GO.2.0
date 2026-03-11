# Task Backlog по `stories-plan.md`

Формат задачи: `TASK-ID` | Story | Компонент | Описание | Зависимости

## Wave 1: Foundation + Auth + Maps

### Epic 0. Foundation
- `TASK-0001` | US-0001 | DevOps | Создать структуру репозитория (`frontend`, `backend`, `infra`) | -
- `TASK-0002` | US-0001 | FE | Инициализировать `React + TS + Vite`, базовый роутинг и layout | TASK-0001
- `TASK-0003` | US-0001 | BE | Инициализировать `.NET 8 Web API`, health endpoint | TASK-0001
- `TASK-0004` | US-0002 | BE/DB | Подключить PostgreSQL и миграции EF Core | TASK-0003
- `TASK-0005` | US-0002 | DB | Создать baseline-таблицы (`User`, `Map`, `MapVersion`, `TerrainObject`, `TerrainObjectType`, `RouteRequest`, `RouteResult`, `ExportJob`) | TASK-0004
- `TASK-0006` | US-0003 | BE | Ввести единый формат ошибок API (problem details) | TASK-0003
- `TASK-0007` | US-0003 | BE | Добавить correlation-id middleware и audit logs | TASK-0003
- `TASK-0008` | US-0004 | DevOps | Настроить CI: lint/test/build frontend+backend | TASK-0002, TASK-0003

### Epic 1. Auth
- `TASK-1001` | US-1001 | BE | Реализовать `POST /auth/register` + DTO валидация | TASK-0005
- `TASK-1002` | US-1001 | BE | Добавить хэширование паролей (`Argon2`/`BCrypt`) | TASK-1001
- `TASK-1003` | US-1002 | BE | Реализовать `POST /auth/login` (JWT access/refresh) | TASK-1002
- `TASK-1004` | US-1003 | BE | Реализовать refresh endpoint + ротация refresh token | TASK-1003
- `TASK-1005` | US-1004 | BE | Реализовать reset password flow (request/confirm) | TASK-1003
- `TASK-1006` | US-1005 | BE | RBAC `User/Admin` + policy-based authorization | TASK-1003
- `TASK-1007` | US-1005 | BE | Tenant-изоляция по ownerId во всех data query | TASK-1006
- `TASK-1008` | US-1001/1002 | FE | UI формы login/register/recover password | TASK-0002, TASK-1003
- `TASK-1009` | US-1002/1003 | FE | Auth state, token refresh interceptor, logout | TASK-1008, TASK-1004

### Epic 2. Maps
- `TASK-2001` | US-2001 | BE | Реализовать `POST /maps/upload` (multipart) | TASK-1007
- `TASK-2002` | US-2001 | BE | Валидация `PNG/JPEG`, размера, mime | TASK-2001
- `TASK-2003` | US-2001 | BE | Реализовать storage adapter (local/S3-compatible) | TASK-2001
- `TASK-2004` | US-2002 | BE | Реализовать `GET /maps` и `GET /maps/{id}` | TASK-2001
- `TASK-2005` | US-2002 | FE | Экран списка карт + статусы (`Uploaded`, `Digitized`, `Edited`, `Ready`) | TASK-2004
- `TASK-2006` | US-2001 | FE | UI загрузки карты с прогрессом и валидацией | TASK-2005
- `TASK-2007` | US-2003 | BE/DB | Создать логику `MapVersion` и переключения активной версии | TASK-2001
- `TASK-2008` | US-2003 | FE | UI просмотра версий карты | TASK-2007

## Wave 2: Digitization + Editor

### Epic 3. Digitization
- `TASK-3001` | US-3001 | BE | Реализовать `POST /maps/{id}/digitize` (job start) | TASK-2007
- `TASK-3002` | US-3001 | BE | CV baseline pipeline (цветовые классы + морфология + контуры) | TASK-3001
- `TASK-3003` | US-3002 | BE | Маппинг 5 классов объектов и 3 геометрий | TASK-3002
- `TASK-3004` | US-3001 | DB | Сохранение объектов с `source=auto`, status update карты | TASK-3003
- `TASK-3005` | US-3003 | BE | Сервис метрик качества (Macro F1, IoU) | TASK-3003
- `TASK-3006` | US-3004 | BE | Повторный запуск оцифровки по выбранной версии | TASK-3004
- `TASK-3007` | US-3001 | FE | Кнопка запуска оцифровки + polling статуса | TASK-3001

### Epic 4. Editor (react-konva)
- `TASK-4001` | US-4001 | FE | Интегрировать `react-konva` canvas слой | TASK-2006
- `TASK-4002` | US-4001 | FE | Реализовать layer switcher: исходник/оцифровка/граф/маршрут | TASK-4001
- `TASK-4003` | US-4002 | FE | Инструменты добавления объектов (точка/линия/полигон) | TASK-4001
- `TASK-4004` | US-4002 | FE | Выбор/удаление/смена класса и типа объекта | TASK-4003
- `TASK-4005` | US-4003 | FE | Редактирование геометрии (drag vertices) | TASK-4003
- `TASK-4006` | US-4003 | FE | Редактирование коэффициента проходимости в свойствах | TASK-4004
- `TASK-4007` | US-4002/4003 | BE | API сохранения правок `PUT /maps/{id}/objects` | TASK-3004
- `TASK-4008` | US-4004 | FE | Undo/redo стек в сессии редактора | TASK-4003
- `TASK-4009` | US-4003 | FE/BE | Сохранение правок как новая `MapVersion` | TASK-4007, TASK-2007

### Epic 5. Terrain types
- `TASK-5001` | US-5001 | BE/DB | Seed системных типов объектов | TASK-0005
- `TASK-5002` | US-5002 | BE | API CRUD пользовательских типов `terrain-types` | TASK-1007
- `TASK-5003` | US-5002 | FE | UI справочника типов (list/create/edit/delete) | TASK-5002
- `TASK-5004` | US-5001/5002 | FE | Подключить типы в редакторе оцифровки | TASK-5003, TASK-4004

## Wave 3: Routing + Explainability

### Epic 6. Graph & Routing
- `TASK-6001` | US-6001 | BE | Построение графа из оцифровки (узлы/ребра/веса) | TASK-4009
- `TASK-6002` | US-6001 | BE | Учет непроходимых зон и штрафов покрытий | TASK-6001
- `TASK-6003` | US-6002 | BE | Реализовать `POST /routes/calculate` (маршрут по точкам) | TASK-6002
- `TASK-6004` | US-6002 | FE | UI выбора точек маршрута на карте и в правой панели | TASK-4001
- `TASK-6005` | US-6002 | BE | Базовый профиль спортсмена (вес времени/безопасности) | TASK-6003
- `TASK-6006` | US-6003 | BE | Расчет `top-3` маршрутов | TASK-6003
- `TASK-6007` | US-6004 | BE | Алгоритм diversity (порог overlap + rerank) | TASK-6006
- `TASK-6008` | US-6005 | BE | Статусы long-running route job + progress | TASK-6006
- `TASK-6009` | US-6005 | FE | Progress bar и состояния route calculation | TASK-6008
- `TASK-6010` | US-6002/6003 | FE | Отрисовка top-3 маршрутов и карточек сравнения | TASK-6006

### Epic 7. Explainability
- `TASK-7001` | US-7001 | BE | Формирование explainability payload по каждому маршруту | TASK-6006
- `TASK-7002` | US-7001 | FE | Блок «почему этот маршрут» в правой панели | TASK-7001, TASK-6010
- `TASK-7003` | US-7002 | FE | Цветовая индикация риска/стоимости сегментов + легенда | TASK-6010

## Wave 4: History + Export + Hardening

### Epic 8. History
- `TASK-8001` | US-8001 | BE/DB | Сохранение RouteRequest/RouteResult с привязкой к `MapVersion` | TASK-6006
- `TASK-8002` | US-8001 | BE | Реализовать `GET /maps/{id}/routes/history` | TASK-8001
- `TASK-8003` | US-8001 | FE | Экран истории маршрутов по карте | TASK-8002
- `TASK-8004` | US-8002 | FE/BE | Кнопка «повторить запуск» из истории | TASK-8003, TASK-6003

### Epic 9. Export
- `TASK-9001` | US-9001 | BE | Реализовать экспорт PNG `POST /routes/{id}/export?format=png` | TASK-8001
- `TASK-9002` | US-9002 | BE | Реализовать экспорт PDF (QuestPDF) | TASK-9001
- `TASK-9003` | US-9001/9002 | FE | UI экспорта и скачивания файлов | TASK-9001
- `TASK-9004` | US-9001/9002 | BE | Включить в экспорт карту, маршрут, точки и легенду | TASK-9001, TASK-9002

### Epic 10. UX
- `TASK-10001` | US-10001 | FE | Собрать основной рабочий экран: карта центр, правая панель | TASK-6004, TASK-6010
- `TASK-10002` | US-10002 | FE | Темная тема и red-orange акценты (design tokens) | TASK-0002
- `TASK-10003` | US-10003 | FE | Empty/loading/error/success состояния на ключевых экранах | TASK-2005, TASK-6009

### Epic 11. Security/Operations
- `TASK-11001` | US-11001 | BE | Жесткая валидация upload (mime/ext/size) + deny list | TASK-2002
- `TASK-11002` | US-11002 | DevOps/DB | Ежедневный backup job + retention policy | TASK-0005
- `TASK-11003` | US-11002 | DevOps/DB | Документированный restore test в test-env | TASK-11002
- `TASK-11004` | US-11003 | DevOps | Метрики uptime/error-rate/latency + dashboard | TASK-0003
- `TASK-11005` | US-11003 | BE | Технические метрики времени digitize/route/export | TASK-3001, TASK-6003, TASK-9001

### Epic 12. Testing & Acceptance
- `TASK-12001` | US-12001 | QA/BE | Unit тесты функции стоимости маршрута | TASK-6002
- `TASK-12002` | US-12001 | QA/BE | Интеграционные тесты A*/top-3/diversity | TASK-6007
- `TASK-12003` | US-12002 | QA/FE | E2E сценарий: upload -> digitize -> edit -> route -> history -> export | TASK-9003, TASK-8004
- `TASK-12004` | US-12003 | QA/PO | Acceptance checklist по MVP из `prd.md` | TASK-12003
- `TASK-12005` | US-12003 | QA/DevOps | Отчет о готовности релиза (SLA, quality, regressions) | TASK-11004, TASK-12004

## Критический путь MVP (обязательно до первого релиза)
- Foundation/Auth/Isolation: `TASK-0001..0007`, `TASK-1001`, `TASK-1002`, `TASK-1003`, `TASK-1006`, `TASK-1007`
- Maps/Versioning: `TASK-2001`, `TASK-2002`, `TASK-2003`, `TASK-2004`, `TASK-2007`
- Digitize+Edit: `TASK-3001`, `TASK-3002`, `TASK-3003`, `TASK-3004`, `TASK-4001..4007`, `TASK-4009`
- Routing+Top3+Diversity: `TASK-6001..6007`, `TASK-6009`, `TASK-6010`, `TASK-7001`, `TASK-7002`
- History+Export: `TASK-8001`, `TASK-8002`, `TASK-8003`, `TASK-9001`, `TASK-9002`, `TASK-9003`
- Acceptance: `TASK-12001`, `TASK-12002`, `TASK-12003`, `TASK-12004`
