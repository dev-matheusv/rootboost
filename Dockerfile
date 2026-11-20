# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer cache) — copy csproj/sln then restore.
# Root.Ia.* vem do GitHub Packages privado. O Railway só entrega variável no build por ARG; ela existe só neste estágio
# (a imagem final começa do zero no estágio runtime) e nunca vai pra arquivo: o nuget.config guarda só o nome.
ARG ROOTIA_PACKAGES_TOKEN
COPY nuget.config ./
COPY RootBoost.sln ./
COPY src/RootBoost.Domain/RootBoost.Domain.csproj src/RootBoost.Domain/
COPY src/RootBoost.Application/RootBoost.Application.csproj src/RootBoost.Application/
COPY src/RootBoost.Infrastructure/RootBoost.Infrastructure.csproj src/RootBoost.Infrastructure/
COPY src/RootBoost.Api/RootBoost.Api.csproj src/RootBoost.Api/
COPY tests/RootBoost.Application.Tests/RootBoost.Application.Tests.csproj tests/RootBoost.Application.Tests/
RUN ROOTIA_PACKAGES_TOKEN="$ROOTIA_PACKAGES_TOKEN" dotnet restore src/RootBoost.Api/RootBoost.Api.csproj

# Copy the rest and publish.
COPY . .
RUN dotnet publish src/RootBoost.Api/RootBoost.Api.csproj -c Release -o /app --no-restore

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Banco: Postgres pela DATABASE_URL que o Railway injeta (RB-29: sem SQLite).
# Bind to the port the platform provides (Railway/Fly set $PORT). Default 8080 locally.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RootBoost.Api.dll"]
