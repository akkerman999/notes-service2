# ---------- Этап 1: сборка приложения ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Сначала копируем только .csproj и восстанавливаем зависимости,
# чтобы Docker кэшировал этот слой
COPY Notes.Api.csproj ./
RUN dotnet restore

# Затем копируем остальной код и публикуем приложение
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---------- Этап 2: лёгкий образ для запуска ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

# Приложение слушает порт 8080 внутри контейнера
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Notes.Api.dll"]
