// TL para formatı: binlik nokta, kuruş virgül — 1.308.800,00

export function fmt(n: number): string {
  return n.toLocaleString("tr-TR", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

// İşaretli format: +145.000,00 / -48.200,00
export function sfmt(n: number): string {
  return (n < 0 ? "-" : "+") + fmt(Math.abs(n));
}
