# ---- 1) React derleme ----
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm ci
COPY web/ ./
# Aynı-origin: VITE_API_URL boş → istekler /api'ye relative gider.
RUN npm run build

# ---- 2) .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Kasa.Core/Kasa.Core.csproj Kasa.Core/
COPY Kasa.Api/Kasa.Api.csproj Kasa.Api/
RUN dotnet restore Kasa.Api/Kasa.Api.csproj
COPY Kasa.Core/ Kasa.Core/
COPY Kasa.Api/ Kasa.Api/
RUN dotnet publish Kasa.Api/Kasa.Api.csproj -c Release -o /app/publish

# ---- 3) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
# React derleme çıktısını wwwroot'a koy (placeholder index.html ezilir).
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
