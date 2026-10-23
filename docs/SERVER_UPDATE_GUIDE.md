# Полное руководство: Как заливать обновления сборки и модов на сервер

В данном руководстве подробно описано, как обновить клиентскую сборку сервера **Servant Server (Fabric)**: добавить новые моды, обновить существующие, изменить конфиги и выкатить обновление всем игрокам через автообновление в лаунчере.

---

## 1. Как работает система автообновления сборки

В лаунчере **Servant Launcher** встроен сервис `ServerPackUpdateService`:

1. **Автоматическая проверка**:
   - При каждом запуске лаунчера (через 8 секунд после старта) и далее **каждые 30 минут** в фоне лаунчер отправляет легкий GET-запрос на:
     `https://servant-ru.online/api/serverpack/version.json`
2. **Сравнение версий**:
   - Лаунчер сравнивает поле `"version"` из `version.json` с текущей версией серверного профиля у игрока (`ServerPackVersion` в `profiles.json`).
   - Если версия на сервере отличается (например, на сервере `1.0.2`, а у игрока `1.0.1`), лаунчер начинает загрузку обновления.
3. **Безопасное применение (Smart Manifest)**:
   - Лаунчер скачивает файл `servant_server.mrpack`.
   - **Моды игрока в полной безопасности**: лаунчер удаляет **только те старые системные файлы**, которые были в предыдущей версии серверной сборки, но отсутствуют в новой.
   - Если игрок сам докинул в папку `mods/` свои моды (шейдеры, мини-карту, индикаторы здоровья), лаунчер **не трогает и не удаляет** их!

---

## 2. Что нужно положить на сервер

На веб-сервере (в директории `/var/www/servant-ru.online/api/serverpack/`) должны лежать всего **два файла**:

```text
/api/serverpack/
├── version.json             <-- Текстовый манифест с номером версии и ссылкой
└── servant_server.mrpack     <-- Архив сборки со всеми модами и конфигами
```

### Формат файла `version.json`
```json
{
  "version": "1.0.2",
  "url": "https://servant-ru.online/api/serverpack/servant_server.mrpack",
  "notes": "Обновлены Sodium и Fabric API, добавлен мод на голосовой чат Simple Voice Chat"
}
```
* **version**: строка версии (например: `1.0.2`, `1.1.0` или дата `2026.09.18`). При изменении этой строки лаунчер сразу поймет, что вышло обновление.
* **url**: прямая ссылка на скачивание `.mrpack`.
* **notes**: краткое описание изменений (опционально).

---

## 3. Как собрать обновление сборки (.mrpack)

Есть **три удобных способа** сформировать файл `servant_server.mrpack`:

### Способ 1: Автоматический скрипт из проекта (Рекомендуемый)

В репозитории лаунчера создан готовый PowerShell-скрипт: `tools/pack_server.ps1`.

1. Откройте лаунчер на своем компьютере, перейдите в сборку **Servant Server**, настройте моды и конфиги (добавьте нужные `.jar`, удалите старые, проверьте запуск игры).
2. Откройте PowerShell в папке проекта `ServantLauncher` и выполните:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\pack_server.ps1 -Version "1.0.2" -Notes "Обновление модов"
   ```
3. Скрипт автоматически:
   - Найдет папку вашей серверной сборки в `%APPDATA%\.servantlauncher\instances\...`.
   - Соберет все моды (`mods/*.jar`), конфиги (`config/`), ресурспаки и настройки в `overrides/`.
   - Сгенерирует правильный манифест `modrinth.index.json`.
   - Упакует всё в `dist/serverpack/servant_server.mrpack`.
   - Создаст готовый `dist/serverpack/version.json` с указанной версией.

Файлы готовы к отправке на сервер в папке: `dist/serverpack/`!

---

### Способ 2: Экспорт прямо из интерфейса лаунчера

1. Запустите лаунчер и перейдите во вкладку **«Сборки»**.
2. Откройте сборку **«Servant Server (1.21.11 Fabric)»**.
3. Перейдите во вкладку **«Настройки сборки»** (иконка шестерёнки).
4. Пролистайте вниз до раздела «Управление сборкой» и нажмите кнопку **«Экспорт в .mrpack»**.
5. Лаунчер вычислит контрольные суммы модов, экспортирует архив в папку **«Загрузки»** (`Downloads`) и откроет её в проводнике.
6. Переименуйте полученный файл в `servant_server.mrpack`.

---

### Способ 3: Ручная сборка ZIP-архива

`.mrpack` — это стандартный ZIP-архив с расширением `.mrpack`. Вы можете собрать его архиватором (7-Zip, WinRAR):

```text
servant_server.mrpack (ZIP)
├── modrinth.index.json
└── overrides/
    ├── mods/
    │   ├── fabric-api-0.141.6+1.21.11.jar
    │   ├── sodium-fabric-0.8.14+mc1.21.11.jar
    │   └── voicechat-fabric-1.21.1-2.5.20.jar
    ├── config/
    │   └── voicechat-client.toml
    └── options.txt
```

Минимальный `modrinth.index.json`:
```json
{
  "formatVersion": 1,
  "game": "minecraft",
  "versionId": "1.0.2",
  "name": "Servant Server",
  "summary": "Серверная сборка Servant",
  "files": [],
  "dependencies": {
    "minecraft": "1.21.11",
    "fabric-loader": "0.19.5"
  }
}
```
*(Все файлы из папки `overrides/` лаунчер автоматически распаковывает напрямую в корень сборки игрока: `overrides/mods/` попадет в `<gameDir>/mods/`, а `overrides/config/` — в `<gameDir>/config/`)*.

---

## 4. Заливка файлов на сервер

### Вариант А: Через WinSCP или FileZilla (Графический интерфейс)

1. Подключитесь по SFTP/SSH к вашему серверу `servant-ru.online`.
2. Перейдите в папку веб-сервера:
   ```text
   /var/www/servant-ru.online/api/serverpack/
   ```
3. Перетащите в эту папку два файла:
   - `servant_server.mrpack`
   - `version.json`
4. Убедитесь, что права на чтение открыты:
   ```bash
   chmod 644 /var/www/servant-ru.online/api/serverpack/*
   ```

---

### Вариант Б: Через консоль Windows (SCP в одну строку)

Если у вас настроен SSH-доступ к серверу:

```powershell
# Заливка архива сборки
scp dist/serverpack/servant_server.mrpack root@servant-ru.online:/var/www/servant-ru.online/api/serverpack/

# Заливка манифеста версий
scp dist/serverpack/version.json root@servant-ru.online:/var/www/servant-ru.online/api/serverpack/
```

---

## 5. Настройка веб-сервера Nginx

Чтобы лаунчер мгновенно видел обновление без задержек прокси-серверов или кэша Nginx, в файле конфигурации Nginx на сервере (например, `/etc/nginx/sites-available/servant-ru.online`) настройте блок для эндпоинта:

```nginx
server {
    server_name servant-ru.online;

    root /var/www/servant-ru.online;

    # Эндпоинт обновлений сборки сервера
    location /api/serverpack/ {
        alias /var/www/servant-ru.online/api/serverpack/;
        
        # Запрещаем кэширование version.json
        add_header Cache-Control "no-cache, no-store, must-revalidate";
        add_header Pragma "no-cache";
        add_header Expires "0";
        
        # Разрешаем CORS-запросы
        add_header Access-Control-Allow-Origin *;
        
        # Разрешаем скачивание больших файлов модов без ограничений
        client_max_body_size 500M;
        
        try_files $uri =404;
    }

    # Эндпоинт обновления самого лаунчера
    location /api/launcher/ {
        alias /var/www/servant-ru.online/api/launcher/;
        add_header Cache-Control "no-cache, no-store, must-revalidate";
        add_header Access-Control-Allow-Origin *;
        try_files $uri =404;
    }
}
```

После изменения конфигурации Nginx перезагрузите его:
```bash
nginx -t && systemctl reload nginx
```

---

## 6. Проверка работы обновления

### Шаг 1: Проверка с сервера или браузера
Откройте в браузере или выполните в консоли:
```bash
curl -i https://servant-ru.online/api/serverpack/version.json
```
Ответ должен быть `200 OK` с содержимым:
```json
{
  "version": "1.0.2",
  "url": "https://servant-ru.online/api/serverpack/servant_server.mrpack",
  "notes": "..."
}
```

Также проверьте доступность скачивания архива:
```bash
curl -I https://servant-ru.online/api/serverpack/servant_server.mrpack
```
Должен вернуть статус `HTTP/2 200` или `HTTP/1.1 200 OK` и размер файла `Content-Length`.

### Шаг 2: Проверка на клиентском ПК
1. Откройте **Servant Launcher**.
2. Лаунчер сделает фоновую проверку:
   - В нижней статусной строке появится уведомление:
     *«Найдена версия 1.0.2. Загрузка .mrpack...»*
   - Лаунчер скачает архив, распакует новые моды и конфиги, удалит только удаленные серверные файлы и обновит статус:
     *«Серверная сборка успешно обновлена до версии 1.0.2!»*
3. Нажмите **«ИГРАТЬ»** — игра запустится с обновленным набором модов.

---

## 7. Готовый чеклист администратора при каждом обновлении

- [ ] **1. Изменили моды/конфиги локально** в клиенте и убедились, что игра успешно запускается и подключается к серверу.
- [ ] **2. Собрали архив сборки**:
      `powershell -ExecutionPolicy Bypass -File .\tools\pack_server.ps1 -Version "1.0.X"`
- [ ] **3. Загрузили файлы на сервер** в `/var/www/servant-ru.online/api/serverpack/`:
      - `servant_server.mrpack`
      - `version.json`
- [ ] **4. Проверили ссылку в браузере**: `https://servant-ru.online/api/serverpack/version.json`.
- [ ] **5. Готово!** Все игроки при следующем открытии лаунчера автоматически получат обновление.
