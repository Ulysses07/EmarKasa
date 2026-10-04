# Temel imajlar etiket + @sha256 özetiyle sabittir (devops-12): aynı Dockerfile her makinede aynı imajı üretir,
# sunucuda önbellekte kalmış eski imaj kullanılmaz. Özet bilinçli güncellenir: docs/deploy/operasyon-runbook.md
# "Temel imajlar" (CI 'base-images' işi daha yeni özet çıkınca uyarır). Özeti elle uydurmayın/kısaltmayın; biçimi
# Kasa.Api.Tests/DepoHijyeniTests denetler. Etiket okunabilirlik içindir; Docker özet varken etiketi yok sayar.
# ---- 1) .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
# Depo kökündeki ortak MSBuild özellikleri (KasaSurumu: /api/surum'un bildirdiği sürüm); yoksa Kasa.Api derlemesi durur.
COPY Directory.Build.props ./
COPY Kasa.Core/Kasa.Core.csproj Kasa.Core/
COPY Kasa.Api/Kasa.Api.csproj Kasa.Api/
RUN dotnet restore Kasa.Api/Kasa.Api.csproj
COPY Kasa.Core/ Kasa.Core/
COPY Kasa.Api/ Kasa.Api/
RUN dotnet publish Kasa.Api/Kasa.Api.csproj -c Release -o /app/publish

# ---- 2) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
# poppler-utils (PDF ekstre okuma) işletim sistemi deposunun o günkü sürümüyle kurulur. Bu katman temel imaj özeti
# değişene kadar önbellekten gelir; paket yamaları için runbook'taki aylık 'build --pull --no-cache' adımı uygulanır.
RUN apt-get update && apt-get install -y --no-install-recommends poppler-utils && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Mevcut bağlama dizinleri root'a ait olabilir. Ayrıcalıksız kimlik ancak veri/yedek sahipliği
# doğrulandıktan sonra seçilir (deploy/README.md); varsayılan çalışma kimliği değişmez.
ARG KASA_RUNTIME_USER=0:0
USER ${KASA_RUNTIME_USER}
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
