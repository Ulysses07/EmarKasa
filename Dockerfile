# Temel imajlar etiket + @sha256 özetiyle sabittir (devops-12): aynı Dockerfile her makinede aynı imajı üretir,
# sunucuda önbellekte kalmış eski imaj kullanılmaz. Özet bilinçli güncellenir: docs/deploy/operasyon-runbook.md
# "Temel imajlar" (CI 'base-images' işi daha yeni özet çıkınca uyarır). Özeti elle uydurmayın/kısaltmayın; biçimi
# Kasa.Api.Tests/DepoHijyeniTests denetler. Etiket okunabilirlik içindir; Docker özet varken etiketi yok sayar.
# ---- 1) .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
WORKDIR /src
COPY Kasa.Core/Kasa.Core.csproj Kasa.Core/
COPY Kasa.Api/Kasa.Api.csproj Kasa.Api/
RUN dotnet restore Kasa.Api/Kasa.Api.csproj
COPY Kasa.Core/ Kasa.Core/
COPY Kasa.Api/ Kasa.Api/
RUN dotnet publish Kasa.Api/Kasa.Api.csproj -c Release -o /app/publish

# ---- 2) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS runtime
# poppler-utils (PDF ekstre okuma) işletim sistemi deposunun o günkü sürümüyle kurulur. Bu katman temel imaj özeti
# değişene kadar önbellekten gelir; paket yamaları için runbook'taki aylık 'build --pull --no-cache' adımı uygulanır.
RUN apt-get update && apt-get install -y --no-install-recommends poppler-utils && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
