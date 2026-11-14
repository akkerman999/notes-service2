#Dockerfile — это инструкция, по которой Docker собирает образ приложения.
# Этап 1: сборка
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build   # образ с SDK для сборки
WORKDIR /src                                      # рабочая папка

COPY Notes.Api.csproj ./                          # копирую только csproj (для кэша)
RUN dotnet restore                                # восстанавливаю зависимости

COPY . ./                                         # копирую весь код
RUN dotnet publish -c Release -o /app/publish --no-restore  # публикую в Release

# Этап 2: запуск
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime  # лёгкий образ для запуска
WORKDIR /app                                          # рабочая папка

COPY --from=build /app/publish ./                     # копирую готовое приложение из build

ENV ASPNETCORE_URLS=http://+:8080                     # слушаем порт 8080
EXPOSE 8080                                           # открываю порт 8080

ENTRYPOINT ["dotnet", "Notes.Api.dll"]                # запуск приложения
