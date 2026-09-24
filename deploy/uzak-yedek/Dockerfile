# Kasa Defteri · sunucu dışı yedek konteyneri (deploy/docker-compose*.yml → kasa-yedek).
# rclone (şifreleme + uzak hedef) + sqlite3 (doğrulama) + jq (durum dosyası).
# Sürüm sabittir: rclone'u güncellemek için etiketi değiştirip `docker compose build kasa-yedek`.
FROM rclone/rclone:1.71.0
RUN apk add --no-cache bash jq sqlite tzdata
COPY kasa-yedek.sh /usr/local/bin/kasa-yedek
# Windows'ta düzenlenip CRLF satır sonuyla gelmiş olabilir: düzelt ve çalıştırılabilir yap.
RUN sed -i 's/\r$//' /usr/local/bin/kasa-yedek && chmod 755 /usr/local/bin/kasa-yedek
# Root olmayan kullanıcı (compose: user 1654) için yazılabilir ev ve önbellek klasörü.
ENV HOME=/tmp XDG_CACHE_HOME=/tmp/.cache
WORKDIR /tmp
ENTRYPOINT ["kasa-yedek"]
CMD ["zamanla"]
