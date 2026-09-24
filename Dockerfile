# ---- 1) .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Kasa.Core/Kasa.Core.csproj Kasa.Core/
COPY Kasa.Api/Kasa.Api.csproj Kasa.Api/
RUN dotnet restore Kasa.Api/Kasa.Api.csproj -r linux-x64
COPY Kasa.Core/ Kasa.Core/
COPY Kasa.Api/ Kasa.Api/
# linux-x64'e özel publish: diğer platformların native kütüphaneleri imaja girmez
# (uygulama katmanı ~74 MB → ~8 MB). Sembol dosyası (.pdb) üretilmez.
RUN dotnet publish Kasa.Api/Kasa.Api.csproj -c Release -r linux-x64 --self-contained false \
      --no-restore -o /app/publish -p:DebugType=none -p:DebugSymbols=false \
 && find /app/publish -name '*.pdb' -delete

# ---- 2) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG KASA_SURUM=bilinmiyor
ENV KASA_SURUM=${KASA_SURUM}
WORKDIR /app
COPY --from=build /app/publish ./
# /data: SQLite DB + yedekler. Root olmayan uygulama kullanıcısına (APP_UID=1654,
# Microsoft imajlarının hazır 'app' kullanıcısı) ait olmalı. Host'tan bind-mount
# edildiğinde host klasörünün sahipliği geçerlidir → deploy/README.md'deki chown adımı.
RUN mkdir -p /data && chown "$APP_UID:$APP_UID" /data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
# aspnet imajında curl/wget yok; bash'in /dev/tcp'si ile /health'e düz HTTP isteği atılır.
# /health DB'ye ulaşamazsa 503 döner → konteyner "unhealthy" görünür (docker ps).
# Not: Docker 'unhealthy' konteyneri kendiliğinden yeniden BAŞLATMAZ; bu yalnız izleme içindir.
HEALTHCHECK --interval=60s --timeout=5s --start-period=90s --retries=3 \
  CMD bash -c "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3 && head -n 1 <&3 | grep -q ' 200 '" || exit 1
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
