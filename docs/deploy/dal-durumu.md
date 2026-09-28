# Dal ve sürüm durumu — Seçenek A uygulandı

> Tespit: 28 Eylül 2026 (devops-1). Değerler o günkü yerel depodan (`git` salt okunur komutları) ve `git ls-remote origin`
> çıktısından alındı; yerelde olmayan PR #11 commit'leri (`8951c1b` ve ataları) `git fetch origin <sha>` ile yalnız nesne
> olarak indirildi, hiçbir dal ya da izlenen uzak ref değiştirilmedi. Bu belge için **hiçbir push, etiket ya da uzak değişiklik yapılmadı.** Karar öncesinde
> aşağıdaki "Durumu yeniden doğrulama" komutlarını tekrar çalıştırın; sayılar o arada değişmiş olabilir.

## Özet

- Canlıdaki sürüm 2.3.0'dır (23 Eylül 2026; [README](../../README.md), [kasa-2.3.md](kasa-2.3.md)). Kodu yalnız `canli-2.3.0-imports` dalında, 297 dosyayı tek seferde ekleyen tek bir commit (`fce8578`) olarak durur; 2.0–2.3'ün ayrıntılı geçmişi depoda yoktur.
- GitHub'ın varsayılan dalı `master`dır (`origin/HEAD → origin/master`) ve canlı kodu **içermez**: 15 Temmuz 2026 ortak atasından (`6a28238`) sonra 76 commit'lik ayrı bir ürün hattıdır (Paket A–F, Android TWA, çekler, `deploy/uzak-yedek`). Canlı kodla farkı 846 dosyadır.
- Hiçbir sürüm etiketi yoktur (yerelde `git tag -l` boş, uzakta `refs/tags` yok). Canlıda hangi commit'in çalıştığı yalnız sunucudaki yayın manifestinden anlaşılır.
- GitHub'daki `canli-2.3.0-imports` 27 Eylül'de ilerlemiştir (`8951c1b`, PR #11: telefon arayüzü `/m/`); yerel dal ve izlenen ref hâlâ `fce8578`'dir. Denetim düzeltmeleri (`denetim-duzeltmeleri`) `fce8578` üzerine 114 commit'tir ve PR #11'i içermez; ikisi de `.github/workflows/ci.yml`'i değiştirir.

## Hatlar

| Hat | Uç | Tarih | Nerede | Canlı kodla ilişkisi |
| --- | --- | --- | --- | --- |
| `origin/master` (GitHub varsayılan dalı) | `ca0d776` (PR #10 birleştirmesi) | 27 Eylül 2026 | GitHub; yerel `C:/Users/burak/source/repos/Kasa-deploy` worktree'si bu hattın `3882379`'unda (24 Eylül, PR #8), başsız (detached), temiz | Ayrık hat: ortak atadan sonra 76 commit; `fce8578` bu hattın atası değil; 846 dosya, +29077/−74858 satır fark |
| Yerel `master` | `dcb149c` | 3 Ağustos 2026 | Yalnız yerel dal adı; `C:/Users/burak/source/repos/Kasa` worktree'sinde açık | `fce8578`'in ebeveyni; `origin/master`'dan 8 önde, 76 geride. Bu 8 commit GitHub'da `canli-2.3.0-imports` üzerinden vardır |
| `canli-2.3.0-imports` | Yerel ve izlenen ref `fce8578`; GitHub'da `8951c1b` | 25 / 27 Eylül 2026 | GitHub + `C:/Users/burak/source/repos/Kasa-canli` worktree'si (temiz) | `fce8578` = canlı 2.3.0 kodu (commit iletisi: "Canlı sürüm 2.3.0-imports (23 Eylül) kodu"). `8951c1b` = `fce8578` + PR #11 (3 commit, 10 dosya, +1528/−2). `8951c1b`'nin sunucuya dağıtılıp dağıtılmadığı depodan belirlenemez |
| `denetim-duzeltmeleri` | `1d31d92` | 28 Eylül 2026 | Yalnız yerel (uzakta dal yok); `C:/Users/burak/source/repos/Kasa-denetim` ve paket worktree'leri (`Kasa-paket/*`, `denetim/*` dalları) | `fce8578` + 114 commit; `8951c1b`'deki 4 commit'i içermez. Canlıda olmayan 3 migration ekler: `20260928000100_KartGecisKurali`, `20260928000200_KartGecisIzi`, `20260929000300_AyRaporAnlikGoruntuleri` |

Ek gözlemler:

- `C:/Users/burak/source/repos/Kasa` worktree'sinde (`master` açık) izlenen 95 dosyada commit edilmemiş değişiklik ve 170 izlenmeyen dosya var. İzlenen dosyalarda `fce8578` ile 204 dosya fark görünüyor; içerik canlı koda benziyor ama birebir değil ve kaynağı doğrulanmadı. Karşılaştırılmadan silinmemeli ya da üzerine yazılmamalıdır.
- Şema yönetimi hatlar arasında farklıdır: canlı hat EF Migrations kullanır (`Kasa.Api/Data/KasaDatabaseInitializer.cs` → `Database.Migrate()`, canlıda 10 migration), `origin/master` `EnsureCreated()` ve `SemaGuncelleyici` kullanır (`Kasa.Api/Data/VeritabaniBaslatici.cs:14`, `:30`) ve `Kasa.Api/Migrations` dizini yoktur.
- `origin/master`'ın Compose şablonları `/data`'ya göreli `./kasa-data` bağlar (`deploy/docker-compose.yml:38`, `deploy/docker-compose.nginx.yml:42`); sunucuda bu yol 2.0 öncesinden korunmuş **eski** veritabanıdır ([deploy/README.md](../../deploy/README.md) "Veri ve yedek dizini kuralı"). Caddy şablonu harici `orderdeck_web` ağını ister.
- `origin/master`'ın CI'ı yalnız `master`'a push ve PR'da çalışır, dağıtım yapmaz. Canlı hattın CI'ına eklenen haftalık temel imaj denetimi ([operasyon-runbook.md](operasyon-runbook.md) "Temel imajlar") GitHub kuralı gereği yalnız varsayılan dalda zamanlanır; varsayılan dal canlı hat olmadıkça çalışmaz.

## Riskler

- **Yanlış hattan dağıtım.** GitHub'dan varsayılan dalı klonlayıp dağıtım adımlarını izleyen biri canlı kod yerine `origin/master`'ı dağıtır. Olası sonuçlar: Nginx şablonuyla sunucudaki eski `./kasa-data` veritabanının bağlanması (yeni kayıtlar yanlış veritabanına yazılır), Caddy şablonunda harici ağ yoksa Compose'un durması; `SemaGuncelleyici`'nin canlı şemayla uyuşmazlıkta açılışı reddetmesi (kesinti) ya da ek tablo/sütunları göç öncesi yedekle eklemesi (canlı koda dönüldüğünde artık olarak kalır); Alış ve aylık gider iş akışlarının kaybolması (bu hatta yok).
- **Kopuk geçmiş.** Canlı hat ile `origin/master` birleştirilirse on binlerce satırlık çakışma çıkar; iki şema yönetimi bir birleştirmeyle uzlaşmaz.
- **İzlenemeyen sürüm.** Etiket olmadığından "canlıda ne var" sorusu depodan yanıtlanamaz; geri dönüş için doğru commit bulunamayabilir.
- **İki yönde ilerleyen canlı hat.** GitHub'daki `canli-2.3.0-imports` (PR #11) ile yerel `denetim-duzeltmeleri` ayrı ilerliyor; ikisi de CI dosyasını değiştiriyor.
- **Yerel çalışma ağacı.** Ana worktree'deki commit edilmemiş içerik tek kopya olabilir.

## Seçenekler

Aşağıdaki komutlar **karar verildikten sonra**, kararı veren kişi tarafından çalıştırılır. Push ve etiket içeren adımlar uzak depoyu değiştirir; her birinden önce "Durumu yeniden doğrulama" komutları çalıştırılır. Komutlar ana depo dizininde (`C:/Users/burak/source/repos/Kasa`) çalışır; `<canli-sha>` canlıda doğrulanan commit'tir (A1).

### A — Canlı hattı korumalı `release/2.x` dalı yap, etiketle, varsayılan dal yap; `origin/master`'ı arşivle (önerilen)

Mevcut hiçbir uzak dal silinmez ya da zorla taşınmaz; yalnız yeni dal ve etiketler eklenir, varsayılan dal değişir.

1. **Canlı commit'i doğrulayın.** Sunucudaki yayın manifestini okuyun (`/opt/kasa/releases/20260923-imports/imports-published.json` ve sonrası). PR #11'in (`/m/` telefon arayüzü) dağıtılıp dağıtılmadığını doğrulayın; dağıtılmadıysa `<canli-sha>` = `fce8578`, dağıtıldıysa `8951c1b`.
2. **Güncel uzak durumu alın** (yalnız yerel izlenen ref'ler güncellenir):
   ```sh
   git fetch origin
   git log --oneline -3 origin/canli-2.3.0-imports
   git rev-parse origin/master
   ```
3. **Canlı kodu etiketleyin:**
   ```sh
   git tag -a v2.3.0 fce8578 -m "Emar Kasa 2.3.0 — 23 Eylül 2026 canlı yayını (imports)"
   git push origin v2.3.0
   ```
   PR #11 ayrıca yayımlandıysa onu da kendi sürümüyle etiketleyin (ör. `git tag -a v2.3.1 8951c1b -m "..."`).
4. **Paralel hattı arşivleyin** (varsayılan dal değişmeden önce):
   ```sh
   git push origin origin/master:refs/heads/arsiv/paralel-hat-2026-09
   git tag -a arsiv/paralel-hat-2026-09-27 origin/master -m "Paket A-F hattı, canlı hattan ayrı (ortak ata 6a28238)"
   git push origin arsiv/paralel-hat-2026-09-27
   ```
   Bu hattaki özelliklerin (Paket A–F, çekler, Excel, değişiklik geçmişi, Android TWA, `deploy/uzak-yedek`) geleceği ürün sahibinin kararıdır (devops-28, [yol haritası](../roadmap.md)).
5. **Yayın dalını açın:**
   ```sh
   git push origin <canli-sha>:refs/heads/release/2.x
   ```
6. **Varsayılan dalı ve korumayı ayarlayın** (GitHub → Settings → General → Default branch, ya da):
   ```sh
   gh repo edit Ulysses07/EmarKasa --default-branch release/2.x
   ```
   Settings → Branches (ya da Rulesets) → `release/2.x`: zorla push ve silme yasak, `Kasa CI` kontrolleri zorunlu, değişiklik yalnız PR ile.
7. **Denetim düzeltmelerini yayın dalına alın:** `denetim-duzeltmeleri` gözden geçirilip onaylandıktan sonra önce GitHub'daki canlı hat birleştirilir, sonra PR açılır:
   ```sh
   git switch denetim-duzeltmeleri
   git merge origin/canli-2.3.0-imports     # PR #11; .github/workflows/ci.yml çakışması elle çözülür
   git push origin denetim-duzeltmeleri
   gh pr create --base release/2.x --head denetim-duzeltmeleri
   ```
8. **Yerel temizlik** (uzak depoya dokunmaz):
   ```sh
   # Ana worktree'deki commit edilmemiş içerik önce ayrı, yalnız yerel bir dalda saklanır; hiçbir şey silinmez.
   # Bu dal uzağa itilmez: izlenmeyen dosyalar sır içerebilir, commit'ten önce 'git status' ile gözden geçirin.
   git -C C:/Users/burak/source/repos/Kasa switch -c yedek/ana-calisma-agaci-2026-09-28
   git -C C:/Users/burak/source/repos/Kasa add -A
   git -C C:/Users/burak/source/repos/Kasa commit -m "Ana çalışma ağacının 28 Eylül durumu (commit edilmemiş içerik)"
   git -C C:/Users/burak/source/repos/Kasa diff --stat fce8578 yedek/ana-calisma-agaci-2026-09-28
   # master artık açık değil; yayın dalına taşınır. dcb149c fce8578'in atası olduğundan commit kaybı yoktur.
   git merge-base --is-ancestor master origin/release/2.x && git branch -f master origin/release/2.x
   # Yanlış hattaki başsız worktree kaldırılır (status boşsa).
   git -C C:/Users/burak/source/repos/Kasa-deploy status --short
   git worktree remove C:/Users/burak/source/repos/Kasa-deploy
   ```
9. **Dağıtım kuralı:** bundan sonra yalnız etiketli commit dağıtılır. Kaynak aktarılmadan önce `git describe --tags --exact-match` başarılı olmalı; etiket ve commit SHA'sı yayın manifestine yazılır. `/api/surum` yanıtına commit SHA'sı eklenmesi ayrı bulgudur (devops-10).

### B — Canlı hattı `master` yap (varsayılan dal adı değişmez)

A'nın 1–4. adımları aynen uygulanır (etiket ve arşiv **önce**). Sonra `master` zorla canlı hatta taşınır; `--force-with-lease` o arada biri `master`'a push ettiyse durur:

```sh
git push --force-with-lease=master:ca0d7761c55d72bc5c9f83c6b2e323dc6d221b31 origin <canli-sha>:refs/heads/master
```

`ca0d776…` 28 Eylül'deki `origin/master`'dır; 2. adımda farklı çıktıysa önce arşivi o SHA ile yineleyin ve buradaki değeri güncelleyin. Açık PR'lar ve `claude/*` dalları eski `master` tabanlıdır; yeniden hedeflenmeleri ya da kapatılmaları gerekir. Ardından A'nın 6 (koruma, `master` için), 7 (`--base master`), 8 ve 9. adımları uygulanır. Klonlayan herkes doğrudan canlı hattı alır; bedeli, `master` geçmişinin bir kez yeniden yazılmasıdır.

### C — İki hattı birleştirmek

Hatlar 846 dosya ve iki farklı şema yönetimiyle ayrışmıştır; doğrudan `git merge` uygulanamaz. Yapılabilecek olan, A ya da B'den sonra `arsiv/paralel-hat-2026-09`'daki özelliklerin ürün kararıyla, canlı hatta tek tek (EF migration'ı ve testleriyle) yeniden uygulanmasıdır. Bu bir dal işlemi değil, özellik başına geliştirme işidir; önce yol haritasında kapsam kararı gerekir.

### D — Şimdilik yalnız eklemeli güvenceler

Varsayılan dal ve `master` değişmeden yalnız A'nın 3 (etiket) ve 4 (arşiv) adımları uygulanır. Hiçbir mevcut ref değişmez; canlı kod etiketle bulunabilir hale gelir. "Yanlış hattan dağıtım" riski sürer: [deploy/README.md](../../deploy/README.md) kurulum ve güncelleme adımlarındaki "varsayılan dal canlı hat değildir" uyarısı yalnız canlı hattın belgelerindedir, varsayılan dalı klonlayan onu görmez.

## Durumu yeniden doğrulama

Salt okunur; dal, etiket ya da uzak depo değişmez (`ls-remote` yalnız okur):

```sh
git ls-remote origin                                   # HEAD, dallar, etiketler (28 Eylül: etiket yok)
git symbolic-ref refs/remotes/origin/HEAD              # refs/remotes/origin/master
git merge-base master origin/master                    # 6a28238 (15 Temmuz 2026)
git rev-list --count master..origin/master             # 76
git rev-list --count origin/master..master             # 8
git merge-base --is-ancestor master fce8578 && echo "master fce8578'in atası"
git merge-base --is-ancestor fce8578 origin/master || echo "canlı kod origin/master'da yok"
git show --stat --format=%s fce8578 | tail -1          # 297 files changed
git diff --shortstat origin/master fce8578             # 846 files changed
git rev-list --count fce8578..denetim-duzeltmeleri     # 114
git tag -l                                             # boş
git worktree list
git -C C:/Users/burak/source/repos/Kasa status --short | wc -l
```

`8951c1b` yerelde yoksa içeriğini görmek için `git fetch origin canli-2.3.0-imports` (izlenen ref'i günceller) çalıştırılır.

## Uygulanan karar (28 Eylül 2026)

Depo sahibi **Seçenek A**'yı seçti. Canlıda çalışan commit `fce8578`'dir: `https://kasa.emarglobal.com/m/` 404 döner ve canlı `index.html` PR #11'in `telefon-yonlendir.js` betiğini içermez; `8951c1b` (PR #11) yayında değildir.

Uygulanan adımlar (etiket, dal ve varsayılan dal komutlarını depo sahibi çalıştırdı; `git ls-remote origin` ile doğrulandı):

| Adım | Sonuç |
| --- | --- |
| A3 — canlı kod etiketi | `v2.3.0` (açıklamalı etiket `271992e`) → `fce8578` |
| A4 — paralel hat arşivi | Dal `arsiv/paralel-hat-2026-09` → `ca0d776`; etiket `arsiv/paralel-hat-2026-09-27` (`643fc8b`) → `ca0d776` |
| A5 — yayın dalı | `release/2.x` → `fce8578` |
| A6 — varsayılan dal | GitHub varsayılan dalı `release/2.x` (`HEAD` → `fce8578`) |
| A6 — dal koruması | `release/2.x`: zorla push ve silme yasak; değişiklik yalnız PR ile (gerekli onay sayısı 0); kurallar yöneticiler için de geçerli (`enforce_admins`) |

`origin/master` (`ca0d776`) ve `canli-2.3.0-imports` (`8951c1b`) değiştirilmedi; hiçbir uzak dal silinmedi ya da zorla taşınmadı.

Açık kalanlar (devops-1'in kapsamı dışında, sırası geldiğinde):

- **Zorunlu CI kontrolü:** `release/2.x` korumasına `Kasa CI` kontrolleri, denetim düzeltmelerinin PR'ında CI yeşil görüldükten sonra zorunlu olarak eklenir.
- **A7:** `denetim-duzeltmeleri`, önce `origin/canli-2.3.0-imports` (PR #11; `.github/workflows/ci.yml` çakışması elle çözülür) birleştirilip `release/2.x`'e PR ile alınır.
- **A8:** ana worktree'deki commit edilmemiş içeriğin yerel yedek dala alınması ve `Kasa-deploy` worktree'sinin kaldırılması depo sahibinin onayıyla yapılır.
- **A9:** bundan sonra yalnız etiketli commit dağıtılır.
- **Paralel hattın özellikleri** (devops-28, yol haritası) ürün sahibinin kararıdır.

**Durum: UYGULANDI.** GitHub varsayılan dalı canlı hattır; varsayılan dalı klonlayan canlı kodu alır. devops-1 kapanmıştır.
