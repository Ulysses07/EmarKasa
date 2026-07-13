import { Link, useNavigate } from "react-router-dom";
import { color, font } from "../theme";

export default function Login() {
  const navigate = useNavigate();

  return (
    <div
      data-screen-label="1g Giriş"
      style={{
        width: 1440,
        height: 700,
        background: color.appBg,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        position: "relative",
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          background:
            "radial-gradient(ellipse 60% 50% at 50% 0%,rgba(30,95,70,0.06),transparent)",
        }}
      />

      <div
        style={{
          width: 384,
          background: color.card,
          border: `1px solid ${color.border}`,
          borderRadius: 16,
          boxShadow: "0 18px 44px rgba(32,38,31,0.08)",
          padding: "30px 30px 24px",
          position: "relative",
        }}
      >
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            gap: 10,
            marginBottom: 22,
          }}
        >
          <div
            style={{
              width: 44,
              height: 44,
              borderRadius: 13,
              background: "#1F2A23",
              color: "#EAF2E7",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              font: `600 22px ${font.mono}`,
            }}
          >
            ₺
          </div>
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              gap: 3,
            }}
          >
            <span style={{ fontSize: 19, fontWeight: 700, letterSpacing: "-0.01em" }}>
              Kasa Defteri
            </span>
            <span style={{ fontSize: 12, color: color.muted }}>
              Haftalık nakit akış defteri
            </span>
          </div>
        </div>

        <div
          style={{
            display: "flex",
            background: "#EFEDE6",
            borderRadius: 10,
            padding: 3,
            gap: 2,
            marginBottom: 18,
          }}
        >
          <button
            style={{
              flex: 1,
              border: "none",
              cursor: "pointer",
              borderRadius: 8,
              padding: "8px 0",
              font: `600 12.5px ${font.sans}`,
              background: color.card,
              color: color.ink,
              boxShadow: "0 1px 3px rgba(32,38,31,0.12)",
            }}
          >
            Editör
          </button>
          <button
            style={{
              flex: 1,
              border: "none",
              cursor: "pointer",
              borderRadius: 8,
              padding: "8px 0",
              font: `600 12.5px ${font.sans}`,
              background: "transparent",
              color: color.sub,
            }}
          >
            İzleyici
          </button>
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <label style={{ fontSize: 11, fontWeight: 600, color: color.sub }}>
              Kullanıcı adı
            </label>
            <input
              defaultValue="selim"
              style={{
                height: 40,
                border: `1px solid ${color.borderInput}`,
                borderRadius: 9,
                background: color.inputBg,
                padding: "0 12px",
                fontSize: 13.5,
                color: color.ink,
                outline: "none",
                minWidth: 0,
              }}
            />
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <label style={{ fontSize: 11, fontWeight: 600, color: color.sub }}>
              Şifre
            </label>
            <input
              defaultValue="••••••••••"
              type="password"
              style={{
                height: 40,
                border: `1px solid ${color.borderInput}`,
                borderRadius: 9,
                background: color.inputBg,
                padding: "0 12px",
                font: `600 14px ${font.mono}`,
                color: color.ink,
                outline: "none",
                minWidth: 0,
              }}
            />
          </div>
          <button
            onClick={() => navigate("/")}
            style={{
              height: 42,
              border: "none",
              borderRadius: 9,
              background: color.green,
              color: color.card,
              font: `600 13.5px ${font.sans}`,
              cursor: "pointer",
              marginTop: 4,
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.background = color.greenDark;
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.background = color.green;
            }}
          >
            Giriş yap
          </button>
        </div>

        <div
          style={{
            borderTop: `1px dashed ${color.border}`,
            marginTop: 18,
            paddingTop: 14,
            textAlign: "center",
          }}
        >
          <span style={{ fontSize: 11.5, color: color.muted }}>
            Ortak mısınız?{" "}
            <Link to="/panel" style={{ fontWeight: 600, textDecoration: "none" }}>
              Tek ortak şifresiyle görüntüleyin
            </Link>
          </span>
        </div>
      </div>

      <span
        style={{
          position: "absolute",
          bottom: 18,
          fontSize: 10.5,
          color: color.faint,
        }}
      >
        © 2026 · özel kayıt defteri
      </span>
    </div>
  );
}
