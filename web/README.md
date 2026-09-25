# Emar Kasa web istemcisi — emekli

Bu dizin önceki React/TypeScript/Vite istemcisini içerir. Aktif ürün `Kasa.App` Windows MAUI istemcisi, `Kasa.Api` servisi ve `Kasa.Api/wwwroot` altındaki yeni mobil web arayüzüdür. Bu eski React dizini dağıtımın parçası değildir; yeni web arayüzünün testleri `Kasa.Api.Ui.Tests` altındadır.

Kod, eski arayüzü ve ürün kararlarını incelemek için korunur. Mevcut API ile tam uyumluluğu veya üretim kullanımı garanti edilmez. Yeni ürün davranışları için [kök README](../README.md) ve aktif istemciyi izleyin.

Eski istemci üzerinde ayrıca çalışılması gerekirse bağımlılıklar `package-lock.json`, komutlar `package.json` içinde tanımlıdır. `npm ci`, `npm test`, `npm run lint` ve `npm run build` bu dizinde çalıştırılabilir; bunların başarılı olması MAUI uygulamasını veya API'yi doğrulamaz.
