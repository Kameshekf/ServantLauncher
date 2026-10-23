# Руководство по настройке сервера обновлений Servant Launcher

Данный документ содержит полное руководство по развертыванию и настройке собственного сервера автообновления для **Servant Launcher**.

URL проверки обновлений по умолчанию:
```text
https://servant-ru.online/api/launcher/version.json
```

---

## 1. Формат манифеста `version.json`

Клиент лаунчера периодически (каждые 30 минут) и при запуске опрашивает данный эндпоинт. Манифест должен отдаваться с HTTP заголовком `Content-Type: application/json; charset=utf-8`.

### Пример `version.json`:
```json
{
  "version": "1.2.0",
  "url": "https://servant-ru.online/downloads/ServantLauncher_1.2.0.exe",
  "sha256": "3B8C3E9F1A4D5B6C7E8F90123456789ABCDEF0123456789ABCDEF0123456789A",
  "changelog": "• Возвращена панель запуска и быстрый выбор сборок на главном экране\n• Плавная анимация масштабирования логотипа при наведении\n• Интеграция с сервисом mclo.gs для удобной отправки логов\n• Новый экран параметров Java по стандарту Freesm\n• Автоматическое скачивание и импорт сборок с Modrinth (.mrpack)\n• Выделение и копирование строк в консоли",
  "minLauncherVersion": "1.0.0"
}
```

### Описание полей:
| Поле | Тип | Описание |
|---|---|---|
| `version` | string | Версия релиза в формате `X.Y.Z` (например, `1.2.0`). Если удаленная версия старше текущей версии лаунчера, запускается скачивание обновления. |
| `url` | string | Прямая ссылка на скачивание исполняемого файла обновления (.exe или установщика). |
| `sha256` | string | Контрольная сумма SHA-256 файла для проверки целостности перед заменой (hex в верхнем или нижнем регистре). |
| `changelog` | string | Список изменений (отображается пользователю при уведомлении об обновлении). |
| `minLauncherVersion` | string | (Опционально) Минимально поддерживаемая версия. |

---

## 2. Настройка веб-сервера (Nginx)

Разместите файлы на сервере, например в директории `/var/www/servant-launcher/`:
```text
/var/www/servant-launcher/
├── api/
│   └── launcher/
│       └── version.json
└── downloads/
    ├── ServantLauncher_1.2.0.exe
    └── ServantSetup_1.2.0.exe
```

### Конфигурация Nginx (`/etc/nginx/sites-available/servant-ru.online`):
```nginx
server {
    listen 80;
    server_name servant-ru.online www.servant-ru.online;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name servant-ru.online www.servant-ru.online;

    ssl_certificate /etc/letsencrypt/live/servant-ru.online/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/servant-ru.online/privkey.pem;

    root /var/www/servant-launcher;
    index index.html;

    # API проверки версии (отключаем кеширование браузером и прокси)
    location /api/launcher/version.json {
        default_type application/json;
        add_header Cache-Control "no-store, no-cache, must-revalidate, proxy-revalidate, max-age=0";
        add_header Access-Control-Allow-Origin *;
        try_files $uri =404;
    }

    # Раздача исполняемых файлов обновления
    location /downloads/ {
        add_header Content-Disposition 'attachment';
        sendfile on;
        tcp_nopush on;
        keepalive_timeout 65;
    }
}
```

После редактирования примените конфигурацию:
```bash
sudo nginx -t && sudo systemctl reload nginx
```

---

## 3. Настройка веб-сервера (Apache)

Если используется Apache, добавьте в `.htaccess` в папке `/api/launcher/`:
```apache
<IfModule mod_headers.c>
    Header set Access-Control-Allow-Origin "*"
    Header set Cache-Control "no-cache, no-store, must-revalidate"
    Header set Pragma "no-cache"
    Header set Expires 0
</IfModule>
```

---

## 4. Скрипт публикации нового релиза

### PowerShell (Windows):
```powershell
$version = "1.2.0"
$exePath = "C:\idea\ServantLauncher\publish_launcher\ServantLauncher.exe"

# Вычисление контрольной суммы SHA256
$hash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash

# Генерация version.json
$json = @{
    version = $version
    url = "https://servant-ru.online/downloads/ServantLauncher_$version.exe"
    sha256 = $hash
    changelog = "Релиз обновления Servant Launcher $version"
} | ConvertTo-Json -Depth 5

Set-Content -Path ".\version.json" -Value $json -Encoding UTF8
Write-Host "version.json создан с SHA256: $hash"
```

### Bash (Linux):
```bash
#!/bin/bash
VERSION="1.2.0"
FILE="/var/www/servant-launcher/downloads/ServantLauncher_${VERSION}.exe"

SHA256=$(sha256sum "$FILE" | awk '{print toupper($1)}')

cat <<EOF > /var/www/servant-launcher/api/launcher/version.json
{
  "version": "${VERSION}",
  "url": "https://servant-ru.online/downloads/ServantLauncher_${VERSION}.exe",
  "sha256": "${SHA256}",
  "changelog": "Автоматическое обновление до версии ${VERSION}"
}
EOF

echo "Манифест обновлен для версии ${VERSION} (${SHA256})"
```

---

## 5. Как клиент обрабатывает автообновление

1. Клиент выполняет `GET https://servant-ru.online/api/launcher/version.json` с User-Agent `servant_launcher/1.2.0`.
2. Сравнивает полученную версию `version` со своей текущей сборкой.
3. Если версия новее:
   - В нижней глобальной строке (`GlobalBottomStatusBar`) отображается прогресс загрузки нового файла.
   - Файл скачивается во временный каталог `%TEMP%\ServantLauncher_update.exe`.
   - Запускается скрипт замены и перезапуска лаунчера.
