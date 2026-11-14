using System.Text.Encodings.Web;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Строка подключения приходит из переменной окружения CONNECTION_STRING (см. docker-compose.yml).
// Хост в ней — имя сервиса "db", а не localhost.
var connectionString = builder.Configuration["CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=app;Username=postgres;Password=1234";

// Чтобы кириллица в JSON-ответах не превращалась в \uXXXX
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

var app = builder.Build();

await InitDatabaseAsync(app);

// ---------- Проверка, что сервис и база живы ----------
app.MapGet("/health", async (NpgsqlDataSource db) =>
{
    try
    {
        await using var cmd = db.CreateCommand("SELECT 1");
        await cmd.ExecuteScalarAsync();
        return Results.Ok(new { status = "ok", database = "connected", time = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Нет подключения к базе данных");
        return Results.Json(new { status = "error", database = "unavailable" }, statusCode: 503);
    }
});

// ---------- READ: список всех заметок ----------
app.MapGet("/notes", async (NpgsqlDataSource db) =>
{
    var list = new List<Note>();
    await using var cmd = db.CreateCommand(
        "SELECT id, title, content, category, pinned, updated_at FROM notes ORDER BY id");
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        list.Add(ReadNote(reader));

    app.Logger.LogInformation("Запрошен список заметок, всего: {Count}", list.Count);
    return Results.Ok(list);
});

// ---------- READ: одна заметка ----------
app.MapGet("/notes/{id:int}", async (int id, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand(
        "SELECT id, title, content, category, pinned, updated_at FROM notes WHERE id = @id");
    cmd.Parameters.AddWithValue("id", id);
    await using var reader = await cmd.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
        return Results.NotFound(new { error = $"Заметка {id} не найдена" });

    return Results.Ok(ReadNote(reader));
});

// ---------- CREATE: добавить заметку ----------
app.MapPost("/notes", async (NewNote input, NpgsqlDataSource db) =>
{
    if (string.IsNullOrWhiteSpace(input.Title))
        return Results.BadRequest(new { error = "Поле title обязательно" });

    await using var cmd = db.CreateCommand("""
        INSERT INTO notes (title, content, category, pinned)
        VALUES (@title, @content, @category, @pinned)
        RETURNING id, title, content, category, pinned, updated_at
        """);
    cmd.Parameters.AddWithValue("title", input.Title);
    cmd.Parameters.AddWithValue("content", input.Content ?? "");
    cmd.Parameters.AddWithValue("category", input.Category ?? "без категории");
    cmd.Parameters.AddWithValue("pinned", input.Pinned ?? false);

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    var note = ReadNote(reader);

    app.Logger.LogInformation("Создана заметка id={Id}: {Title}", note.Id, note.Title);
    return Results.Created($"/notes/{note.Id}", note);
});

// ---------- UPDATE: изменить заметку ----------
app.MapPut("/notes/{id:int}", async (int id, NewNote input, NpgsqlDataSource db) =>
{
    if (string.IsNullOrWhiteSpace(input.Title))
        return Results.BadRequest(new { error = "Поле title обязательно" });

    await using var cmd = db.CreateCommand("""
        UPDATE notes
        SET title = @title, content = @content, category = @category,
            pinned = @pinned, updated_at = now()
        WHERE id = @id
        RETURNING id, title, content, category, pinned, updated_at
        """);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("title", input.Title);
    cmd.Parameters.AddWithValue("content", input.Content ?? "");
    cmd.Parameters.AddWithValue("category", input.Category ?? "без категории");
    cmd.Parameters.AddWithValue("pinned", input.Pinned ?? false);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound(new { error = $"Заметка {id} не найдена" });

    var note = ReadNote(reader);
    app.Logger.LogInformation("Обновлена заметка id={Id}: {Title}", note.Id, note.Title);
    return Results.Ok(note);
});

// ---------- DELETE: удалить заметку ----------
app.MapDelete("/notes/{id:int}", async (int id, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("DELETE FROM notes WHERE id = @id");
    cmd.Parameters.AddWithValue("id", id);
    var deleted = await cmd.ExecuteNonQueryAsync();

    if (deleted == 0)
        return Results.NotFound(new { error = $"Заметка {id} не найдена" });

    app.Logger.LogInformation("Удалена заметка id={Id}", id);
    return Results.NoContent();
});

app.Run();

// ---------- вспомогательные функции ----------

static Note ReadNote(NpgsqlDataReader r) =>
    new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetBoolean(4), r.GetDateTime(5));

// Создаёт таблицу при старте. depends_on не гарантирует, что PostgreSQL уже готов
// принимать подключения, поэтому несколько раз пробуем с паузой.
static async Task InitDatabaseAsync(WebApplication app)
{
    var db = app.Services.GetRequiredService<NpgsqlDataSource>();
    const int maxAttempts = 15;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await using var cmd = db.CreateCommand("""
                CREATE TABLE IF NOT EXISTS notes (
                    id         SERIAL PRIMARY KEY,
                    title      TEXT      NOT NULL,
                    content    TEXT      NOT NULL DEFAULT '',
                    category   TEXT      NOT NULL DEFAULT 'без категории',
                    pinned     BOOLEAN   NOT NULL DEFAULT false,
                    updated_at TIMESTAMP NOT NULL DEFAULT now()
                )
                """);
            await cmd.ExecuteNonQueryAsync();
            app.Logger.LogInformation("База данных готова, таблица notes создана или уже существует");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning("База данных пока недоступна (попытка {Attempt}/{Max}): {Message}",
                attempt, maxAttempts, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

record Note(int Id, string Title, string Content, string Category, bool Pinned, DateTime UpdatedAt);
record NewNote(string Title, string? Content, string? Category, bool? Pinned);
