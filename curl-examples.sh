#!/usr/bin/env bash
# Примеры CRUD-запросов к Notes API (нужен запущенный docker compose up -d).
# Запуск: bash curl-examples.sh
BASE=http://localhost:8080

echo "--- health ---"
curl -s $BASE/health; echo

echo "--- CREATE: добавить заметку ---"
curl -s -X POST $BASE/notes \
  -H "Content-Type: application/json" \
  -d '{"title":"Купить продукты","content":"Молоко, хлеб, яйца","category":"личное"}'; echo

echo "--- CREATE: ещё одна ---"
curl -s -X POST $BASE/notes \
  -H "Content-Type: application/json" \
  -d '{"title":"Идеи проекта","content":"Сделать REST API для заметок","category":"учёба","pinned":true}'; echo

echo "--- READ: список ---"
curl -s $BASE/notes; echo

echo "--- READ: одна заметка (id=1) ---"
curl -s $BASE/notes/1; echo

echo "--- UPDATE: изменить заметку id=1 ---"
curl -s -X PUT $BASE/notes/1 \
  -H "Content-Type: application/json" \
  -d '{"title":"Купить продукты","content":"Молоко, хлеб, яйца, сыр","category":"личное","pinned":true}'; echo

echo "--- DELETE: удалить заметку id=2 (ожидается код 204) ---"
curl -s -o /dev/null -w "HTTP %{http_code}\n" -X DELETE $BASE/notes/2

echo "--- READ: список после изменений ---"
curl -s $BASE/notes; echo
