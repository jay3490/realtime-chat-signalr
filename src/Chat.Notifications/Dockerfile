# Build context is the repository root:  docker build -f src/Chat.Notifications/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Chat.Contracts/Chat.Contracts.csproj src/Chat.Contracts/
COPY src/Chat.Domain/Chat.Domain.csproj src/Chat.Domain/
COPY src/Chat.Application/Chat.Application.csproj src/Chat.Application/
COPY src/Chat.Infrastructure/Chat.Infrastructure.csproj src/Chat.Infrastructure/
COPY src/Chat.Notifications/Chat.Notifications.csproj src/Chat.Notifications/
RUN dotnet restore src/Chat.Notifications/Chat.Notifications.csproj

COPY src/ src/
RUN dotnet publish src/Chat.Notifications/Chat.Notifications.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "Chat.Notifications.dll"]
