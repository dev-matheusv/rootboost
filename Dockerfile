# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first (layer cache) — copy csproj/sln then restore.
COPY RootBoost.sln ./
COPY src/RootBoost.Domain/RootBoost.Domain.csproj src/RootBoost.Domain/
COPY src/RootBoost.Application/RootBoost.Application.csproj src/RootBoost.Application/
COPY src/RootBoost.Infrastructure/RootBoost.Infrastructure.csproj src/RootBoost.Infrastructure/
COPY src/RootBoost.Api/RootBoost.Api.csproj src/RootBoost.Api/
COPY tests/RootBoost.Application.Tests/RootBoost.Application.Tests.csproj tests/RootBoost.Application.Tests/
RUN dotnet restore src/RootBoost.Api/RootBoost.Api.csproj

# Copy the rest and publish.
COPY . .
RUN dotnet publish src/RootBoost.Api/RootBoost.Api.csproj -c Release -o /app --no-restore

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# SQLite lives on a persistent volume mounted at /data (configure the volume on the host).
ENV ConnectionStrings__Sqlite="Data Source=/data/rootboost.db"
# Bind to the port the platform provides (Railway/Fly set $PORT). Default 8080 locally.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RootBoost.Api.dll"]
