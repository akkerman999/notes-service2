# Notes API + PostgreSQL (Docker Compose)

Учебный проект: REST API сервиса заметок на **.NET 9 (ASP.NET Core Minimal API)** с базой данных **PostgreSQL**, которые запускаются одной командой через **Docker Compose**.

Схема: `API (контейнер api) → PostgreSQL (контейнер db) + volume db_data`

## Что делает API

CRUD для сущности **«заметка»** (note): создание, чтение, изменение и удаление. Данные хранятся в таблице `notes` в PostgreSQL.

| Операция | Метод  | Путь           | Описание                              |
|----------|--------|----------------|---------------------------------------|
| —        | GET    | `/health`      | Проверка сервиса и подключения к БД   |
| Read     | GET    | `/notes`       | Список всех заметок                   |
| Read     | GET    | `/notes/{id}`  | Одна заметка (404, если не найдена)   |
| Create   | POST   | `/notes`       | Добавить заметку                      |
| Update   | PUT    | `/notes/{id}`  | Изменить заметку                      |
| Delete   | DELETE | `/notes/{id}`  | Удалить заметку (204 No Content)      |

Пример тела запроса для POST/PUT:

```json
{ "title": "Купить продукты", "content": "Молоко, хлеб, яйца", "category": "личное", "pinned": false }
```

Обязательно только поле `title`. Остальные необязательны (по умолчанию `content` пустое, `category` = `без категории`, `pinned` = `false`). При обновлении заметки поле `updated_at` обновляется автоматически.

## Структура проекта

```
├── Program.cs           # код API (CRUD + подключение к PostgreSQL)
├── Notes.Api.csproj     # проект .NET (пакет Npgsql)
├── Dockerfile           # образ API
├── docker-compose.yml   # сервисы api и db, сеть, volume
├── curl-examples.sh     # примеры CRUD-запросов
├── .dockerignore / .gitignore
├── README.md
└── docs/                # вывод команд для проверки
```

## Сервисы в docker-compose.yml

| Сервис | Образ | Что делает |
|--------|-------|-----------|
| `api` | собирается из `Dockerfile` (`build: .`) | API на .NET, порт `8080:8080` |
| `db`  | `postgres:16` | PostgreSQL: база `app`, пользователь `postgres`, пароль `1234` |

Ключевые настройки:

- **environment** — настройки передаются через переменные окружения. API получает строку подключения `CONNECTION_STRING=Host=db;Port=5432;Database=app;Username=postgres;Password=1234`, а PostgreSQL создаёт пользователя и базу из `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`.
- **Подключение по имени сервиса** — в строке подключения `Host=db`, а не `localhost`: внутри контейнера `localhost` — это сам контейнер API, а не соседний контейнер с базой.
- **networks** — оба сервиса в общей сети `notes-net`, поэтому `api` находит `db` по имени.
- **depends_on** — `api` стартует после `db`. Само по себе `depends_on` не гарантирует, что база готова принимать подключения, поэтому добавлены `healthcheck` (`pg_isready`) у `db` и `condition: service_healthy` у `api`. Дополнительно API при старте делает до 15 попыток подключения.
- **volumes** — `db_data:/var/lib/postgresql/data`: данные PostgreSQL хранятся в named volume `db_data` и сохраняются, даже если контейнер удалить.

## Запуск

Нужен запущенный Docker Desktop.

```bash
docker compose up -d --build
docker compose ps
```

В `docker compose ps` должны быть два сервиса: `api` (Up) и `db` (Up, healthy).

## Проверка CRUD

Все запросы сразу — скрипт:

```bash
bash curl-examples.sh
```

Или по отдельности:

```bash
curl http://localhost:8080/health

# Create
curl -X POST http://localhost:8080/notes -H "Content-Type: application/json" -d '{"title":"Купить продукты","content":"Молоко, хлеб, яйца","category":"личное"}'

# Read
curl http://localhost:8080/notes
curl http://localhost:8080/notes/1

# Update
curl -X PUT http://localhost:8080/notes/1 -H "Content-Type: application/json" -d '{"title":"Купить продукты","content":"Молоко, хлеб, яйца, сыр","category":"личное","pinned":true}'

# Delete
curl -i -X DELETE http://localhost:8080/notes/1
```

(в PowerShell используйте `curl.exe`; запросы с JSON удобнее выполнять в Git Bash или Postman)

## Проверка сохранения данных после перезапуска

1. Создайте несколько заметок (см. выше) и посмотрите список: `curl http://localhost:8080/notes`.
2. Удалите контейнеры **без удаления volume**:
   ```bash
   docker compose down
   ```
3. Запустите снова:
   ```bash
   docker compose up -d
   ```
4. Проверьте, что заметки на месте:
   ```bash
   curl http://localhost:8080/notes
   ```
5. Данные можно увидеть и прямо в PostgreSQL:
   ```bash
   docker compose exec db psql -U postgres -d app -c "SELECT * FROM notes;"
   ```

> ⚠️ `docker compose down -v` удаляет и volume `db_data`, то есть все данные базы. Для проверки сохранения используйте `docker compose down` без `-v`.

## Логи и проверка контейнеров

```bash
docker compose ps
docker compose logs
docker compose logs api
docker compose logs db
```

В логах `api` видны сообщения о том, что база готова, и записи о CRUD-операциях.

## Сохранение результатов для сдачи

```bash
docker compose ps > docs/compose-ps.txt
bash curl-examples.sh > docs/crud-check.txt 2>&1
docker compose logs > docs/compose-logs.txt 2>&1
```

Для `docs/persistence-check.txt` выполните шаги из раздела про перезапуск и сохраните вывод `curl http://localhost:8080/notes` до `docker compose down` и после `docker compose up -d`.

## Остановка

```bash
docker compose stop        # остановить сервисы
docker compose down        # остановить и удалить контейнеры (данные сохраняются)
docker compose down -v     # то же + удалить volume с данными
docker compose restart api # перезапустить один сервис
```

## Для защиты

| Вопрос | Ответ |
|--------|-------|
| Какая сущность в CRUD? | Заметка (`note`): заголовок, текст, категория, закрепление, дата изменения |
| Как связана с темой проекта? | Сервис заметок хранит и изменяет заметки пользователя |
| Какие сервисы в compose? | `api` (.NET, собирается из Dockerfile) и `db` (`postgres:16`) |
| Почему PostgreSQL? | Это отдельный сервис базы данных, который запускается в своём контейнере и хранит данные независимо от приложения |
| Как сервисы общаются? | Через общую сеть `notes-net`: API обращается к `db` по имени сервиса |
| Что происходит при `docker compose down` и повторном `up`? | Контейнеры удаляются и создаются заново, но данные в volume `db_data` сохраняются |
| Где логи? | `docker compose logs api` / `docker compose logs db` |
