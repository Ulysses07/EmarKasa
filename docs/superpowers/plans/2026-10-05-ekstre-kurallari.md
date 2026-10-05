# Ekstre sınıflandırma kuralları Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** AI olmadan kişisel kurallarla PDF hareketlerinin türünü ve mevcut kasa/kanal dağılımını önermek.

**Architecture:** Sunucu kalıcı kuralları ve salt okunur öneri motorunu yönetir. İstemciler önerileri mevcut satır editörlerine uygular; finans kaydetme yolu/onayları aynıdır. Kurallar ayrı dosyalarda tutulur.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/SQLite, MAUI Windows, mevcut JavaScript modülleri, xUnit ve Node testleri.

**Spec:** `docs/superpowers/specs/2026-10-05-ekstre-kurallari.md`

## Global Constraints

- Web (telefon dahil), Windows ve ortak istemci aynı kalıcı sunucu kurallarını kullanır.
- Dış servis/API veya yapay zekâ eklenmez; OCR kapsamı değişmez.
- Kurallar boş başlar; göç mevcut belgeleri/finans kayıtlarını değiştirmez.
- Kural finans kaydı üretmez; mevcut önizleme ve mali doğrulamalar korunur.
- Toplu uygulama elle değiştirilen satırları ezmez; satır seçmez.

## Review Focus

- Türkçe harf/sözcük sınırı yanlış işyeri eşleştirmemeli (Task 1).
- Transfer/taksit/ödeme/iade ve uyarılı PDF satırı parser korumalarını aşmamalı (Task 1).
- Oturum/belge değişmesinde eski asenkron sonuç yeni ekrana taşınmamalı (Tasks 2–4).
- Elle düzenlenmiş/kayıtlı/yabancı para satırı toplu öneriyle değişmemeli/seçilmemeli (Tasks 2–4).
- Pasif/silinmiş kanal ve eski sunucu anlaşılır ileti üretmeli; mevcut PDF akışı kullanılabilmeli (Tasks 1–4).

## Task 1: Kalıcı kurallar ve öneri motoru

**Files:** Create `Kasa.Api/Data/EkstreKuralEntity.cs`, `Kasa.Api/EkstreKuralDtos.cs`, `Kasa.Api/EkstreKuralServisi.cs`, `Kasa.Api/EkstreAktarmaEndpoints.Kurallar.cs`, migration/frozen model, `Kasa.Api.Tests/EkstreKuralTests.cs`; modify context, snapshot, endpoint registration, migration tests/route inventory.

**Interfaces:** Produces `EkstreKuralYaz(Guid IstekId, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir, string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif = true)`, `EkstreKuralDto` (Id/Surum + rule fields), `EkstreKuralOnerisi(int SatirNo, string Durum, IReadOnlyList<string> KuralAdlari, string? IslemTuru, string? DagilimTuru, IReadOnlyList<int> KanalIds, string Aciklama)`. Routes beneath `/api/ekstre-aktar`: GET/POST `/kurallar`, PUT/DELETE `/kurallar/{id}` (DELETE query surum), GET `/{id}/oneriler`.

- [x] Step 1: Write real HTTP tests. Missing routes return 404 instead of expected 200. Cover CRUD/stale version/idempotency, Turkish normalization/word boundaries, bank/direction scope, equal outcomes vs conflicts, inactive channel, warnings/foreign currency, no change to cash/document version. Test migration and rule backup persistence.

```csharp
var response = await client.PostAsJsonAsync("/api/ekstre-aktar/kurallar", new {
    istekId = Guid.NewGuid(), surum = 0, ad = "Kargo", kaynak = "Banka", banka = "Akbank",
    aciklamaIcerir = "YURTİÇİ KARGO", yon = "Cikis", islemTuru = "Gider",
    dagilimTuru = "Esit", kanalIds = new[] { 1 }, aktif = true });
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

- [x] Step 2: Run `dotnet test Kasa.Api.Tests --no-restore --filter FullyQualifiedName~EkstreKural`; Expected: FAIL on missing routes.
- [x] Step 3: Implement empty table/frozen migration `20261010000100_EkstreKurallari`, concurrency version, JSON channel IDs, CRUD transactions/idempotency and consistent suggestion read. Normalize words without user regex, evaluate all matching rules, compare result signatures, gate by row safety. Limit 200 rules, names 100 characters, phrases 3–200 characters/at least one 3-character word, 20 distinct active channels.

```csharp
api.MapGet("/{id:int}/oneriler", (int id, KasaDbContext db) => Safe(() =>
    AlisEndpoints.Oku(db, () => Results.Ok(EkstreKuralServisi.Oneriler(db, GetDocument(db, id))))));
```

- [x] Step 4: Update migration counts/inventory; run new tests and migration tests. Expected: PASS, no pending model changes, preserved financial records.
- [x] Step 5: Commit `feat: add persistent statement classification rules`.

## Task 2: Ortak istemci ve öneri uygulama modeli

**Files:** Create `Kasa.ApiClient/EkstreKuralDtos.cs`, `Kasa.App.Core/EkstreAktarmaViewModel.Kurallar.cs`; modify API interface/client, editor model/VM; test API client, App.Core and contract packages.

**Interfaces:** Consumes Task 1 DTO/routes; produces `EkstreKurallarAsync`, `EkstreKuralEkleAsync`, `EkstreKuralDuzenleAsync`, `EkstreKuralSilAsync`, `EkstreOnerilerAsync`; editor `Oneri`, `OneriMetni`, `ElleDegisti`, `OneriyiUygula(bool toplu)`; VM rule list/refresh/bulk/remember/CRUD methods for Task 4.

- [x] Step 1: Add editor/VM behavior and real API contract tests. Single-channel rule fills a pay without selecting; manual edits prevent bulk overwrite; explicit individual application may replace. Preview invalidates, old-server 404 keeps existing flow, late session/document results are discarded. Every interface method has contract coverage.

```csharp
editor.OneriyiUygula(toplu: true);
Assert.Equal("Esit", editor.DagilimTuru?.Kod);
Assert.False(editor.Secili);
```

- [x] Step 2: Run focused App.Core/client tests; Expected: FAIL on missing behavior.
- [x] Step 3: Mirror DTOs/implement routes and compatibility fallbacks. Fetch suggestions separately; original PDF proposals stay intact. Track manual changes including pays; use existing generation/preview invalidation. Remember only single-channel custom shares as equal; multi-channel custom shares require explicit new rule choices.
- [x] Step 4: Run client, App.Core and contract tests; Expected: PASS.
- [x] Step 5: Commit `feat: support statement rule suggestions in shared clients`.

## Task 3: Web ve telefon ekranı

**Files:** Create `Kasa.Api/wwwroot/statement-rules-ui.js`; modify import UI/shared distribution; test `Kasa.Api.Ui.Tests/ui-core.test.mjs` and browser tests.

**Interfaces:** Consumes Task 1 JSON DTO/routes; uses `distribution.set(initial)` for equal allocation. Rule refresh updates proposals without rebuilding/discarding the document draft.

- [x] Step 1: Add behavioral tests: manager/remember, proposal display, bulk fill without selection, conflict, manual edit protection, preview invalidation, late session result.

```javascript
click(buttonNamed('Önerileri uygula'));
assert.equal(control('tur-1').value, 'Gider');
assert.equal(control('sec-1').checked, false);
```

- [x] Step 2: Run focused Node tests; Expected: FAIL because feature controls/behavior are absent.
- [x] Step 3: Implement safe DOM rule forms add/edit/disable/delete with existing modal guards. Per-row proposal/apply/remember and refresh/bulk toolbar; preserve manual edits and invalidate previews on fill. Missing old-server support is optional feature state.
- [x] Step 4: Run Node UI suite, lint/format and desktop/phone browser checks; Expected: PASS, usable dialogs, no automatic writes.
- [x] Step 5: Commit `feat: add web statement rule management`.

## Task 4: Windows ekranı ve son doğrulama

**Files:** Modify desktop import page; create focused rule form helper; README/user docs; App.Core tests; final review package.

**Interfaces:** Consumes Task 2 VM/editor. Form exposes phrase/scope/type/equal channel options/concurrency version; callbacks capture session and preserve changed document after awaits.

- [x] Step 1: Add missing remember/draft/CRUD/late-session behavior tests before UI binding. Run; Expected: FAIL on missing behavior.
- [x] Step 2: Implement Windows manager/remember forms, proposal status and individual/bulk buttons with existing UI patterns; explicit confirmation for rule deletion.
- [x] Step 3: Run all affected .NET suites, web suite and Windows build `dotnet build Kasa.App -f net10.0-windows10.0.19041.0`; Expected: PASS. Document examples and existing PDF limits; no version/deploy assumed.
- [x] Step 4: Commit `feat: add desktop statement rules and usage documentation`.
- [ ] Step 5: Fresh independent whole-branch review against feature base/spec/plan; reproduce/fix material findings with failing-first tests; run final verification. Open/attach feature PR under previously established workflow; merging/deployment require explicit request.
