namespace Kasa.Api.Migrations;

/// <summary>İlk migration'ın değiştirilmeyen SQLite şeması; eski EnsureCreated
/// veritabanlarını aynı başlangıç noktasına veri koruyarak taşımak için de kullanılır.</summary>
internal static class StableSchemaDefinition
{
    internal const string MigrationId = "20260919000100_InitialStableSchema";
    internal const string ProductVersion = "10.0.9";

    internal sealed record Table(string Name, string Definition, string[] Columns,
        IReadOnlyDictionary<string, string>? AdditiveColumns = null)
    {
        internal string Create(string? name = null) => $"CREATE TABLE \"{name ?? Name}\" ({Definition});";
    }

    internal static readonly Table[] Tables =
    [
        new("Kanallar", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Kanallar" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT COLLATE NOCASE NOT NULL, "Aktif" INTEGER NOT NULL,
            "Sira" INTEGER NOT NULL, "AcilisDevri" TEXT NOT NULL
            """, ["Id", "Ad", "Aktif", "Sira", "AcilisDevri"]),
        new("Cariler", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Cariler" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL, "Aktif" INTEGER NOT NULL
            """, ["Id", "Ad", "Aktif"]),
        new("Ayarlar", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Ayarlar" PRIMARY KEY AUTOINCREMENT,
            "TakipBaslangic" TEXT NOT NULL, "KasaAcilisDevri" TEXT NOT NULL,
            "IzleyiciSifreHash" TEXT NULL
            """, ["Id", "TakipBaslangic", "KasaAcilisDevri", "IzleyiciSifreHash"]),
        new("KrediKartlari", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_KrediKartlari" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL, "KesimTarihi" TEXT NOT NULL, "SonOdemeTarihi" TEXT NOT NULL,
            "Limit" TEXT NOT NULL, "Borc" TEXT NOT NULL
            """, ["Id", "Ad", "KesimTarihi", "SonOdemeTarihi", "Limit", "Borc"]),
        new("Islemler", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Islemler" PRIMARY KEY AUTOINCREMENT,
            "Tarih" TEXT NOT NULL, "Cari" TEXT NOT NULL, "TutarTl" TEXT NOT NULL,
            "Kanal" TEXT NOT NULL, "Tip" INTEGER NOT NULL, "Not" TEXT NULL,
            "KrediKartiId" INTEGER NULL, "KanalId" INTEGER NULL,
            CONSTRAINT "FK_Islemler_KrediKartlari_KrediKartiId" FOREIGN KEY ("KrediKartiId") REFERENCES "KrediKartlari" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_Islemler_Kanallar_KanalId" FOREIGN KEY ("KanalId") REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT
            """, ["Id", "Tarih", "Cari", "TutarTl", "Kanal", "Tip", "Not", "KrediKartiId", "KanalId"],
            new Dictionary<string, string> { ["KrediKartiId"] = "INTEGER NULL", ["KanalId"] = "INTEGER NULL" }),
        new("Gelenler", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Gelenler" PRIMARY KEY AUTOINCREMENT,
            "DonemStart" TEXT NOT NULL, "Kanal" TEXT COLLATE NOCASE NOT NULL,
            "TutarTl" TEXT NOT NULL, "KanalId" INTEGER NULL,
            CONSTRAINT "FK_Gelenler_Kanallar_KanalId" FOREIGN KEY ("KanalId") REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT
            """, ["Id", "DonemStart", "Kanal", "TutarTl", "KanalId"],
            new Dictionary<string, string> { ["KanalId"] = "INTEGER NULL" }),
        new("KartOdemeler", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_KartOdemeler" PRIMARY KEY AUTOINCREMENT,
            "KrediKartiId" INTEGER NOT NULL, "Tarih" TEXT NOT NULL, "Tutar" TEXT NOT NULL,
            "Not" TEXT NULL,
            CONSTRAINT "FK_KartOdemeler_KrediKartlari_KrediKartiId" FOREIGN KEY ("KrediKartiId") REFERENCES "KrediKartlari" ("Id") ON DELETE CASCADE
            """, ["Id", "KrediKartiId", "Tarih", "Tutar", "Not"]),
        new("Krediler", """
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Krediler" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL, "CekilenTutar" TEXT NOT NULL, "CekimTarihi" TEXT NOT NULL,
            "TaksitSayisi" INTEGER NOT NULL, "AylikOdeme" TEXT NOT NULL, "OdemeGunu" INTEGER NOT NULL,
            "Kanal" TEXT NOT NULL, "KanalId" INTEGER NULL,
            CONSTRAINT "FK_Krediler_Kanallar_KanalId" FOREIGN KEY ("KanalId") REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT
            """, ["Id", "Ad", "CekilenTutar", "CekimTarihi", "TaksitSayisi", "AylikOdeme", "OdemeGunu", "Kanal", "KanalId"],
            new Dictionary<string, string> { ["KanalId"] = "INTEGER NULL" })
    ];

    internal static readonly (string Name, string Sql)[] Indexes =
    [
        ("IX_Kanallar_Ad", "CREATE UNIQUE INDEX \"IX_Kanallar_Ad\" ON \"Kanallar\" (\"Ad\");"),
        ("IX_Islemler_KrediKartiId", "CREATE INDEX \"IX_Islemler_KrediKartiId\" ON \"Islemler\" (\"KrediKartiId\");"),
        ("IX_Islemler_KanalId", "CREATE INDEX \"IX_Islemler_KanalId\" ON \"Islemler\" (\"KanalId\");"),
        ("IX_KartOdemeler_KrediKartiId", "CREATE INDEX \"IX_KartOdemeler_KrediKartiId\" ON \"KartOdemeler\" (\"KrediKartiId\");"),
        ("IX_Gelenler_KanalId", "CREATE INDEX \"IX_Gelenler_KanalId\" ON \"Gelenler\" (\"KanalId\");"),
        ("IX_Gelenler_DonemStart_KanalId", "CREATE UNIQUE INDEX \"IX_Gelenler_DonemStart_KanalId\" ON \"Gelenler\" (\"DonemStart\", \"KanalId\");"),
        ("IX_Gelenler_DonemStart_Kanal", "CREATE UNIQUE INDEX \"IX_Gelenler_DonemStart_Kanal\" ON \"Gelenler\" (\"DonemStart\", \"Kanal\");"),
        ("IX_Krediler_KanalId", "CREATE INDEX \"IX_Krediler_KanalId\" ON \"Krediler\" (\"KanalId\");")
    ];
}
