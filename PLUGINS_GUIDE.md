# Руководство по разработке плагинов для Servant Launcher

Servant Launcher поддерживает модульную систему плагинов. Вы можете расширять функционал лаунчера: перехватывать запуск Minecraft, добавлять автоматизацию, синхронизировать сохранения, менять темы или кастомизировать интерфейс.

---

## 📁 Где находятся плагины?

Все плагины хранятся в папке:
```
%AppData%\.servantlauncher\plugins\
```
*(например: `C:\Users\<Имя>\AppData\Roaming\.servantlauncher\plugins`)*

---

## 🛠️ Как создать свой плагин (Пошаговый пример на C#)

### Шаг 1: Создайте проект Class Library
Создайте стандартную библиотеку классов .NET 9.0 (или .NET 8.0+):

```bash
dotnet new classlib -n MyServantPlugin -f net9.0
```

### Шаг 2: Добавьте ссылку на ServantLauncher
В файл `MyServantPlugin.csproj` добавьте зависимость от `ServantLauncher.dll`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="ServantLauncher">
      <HintPath>path\to\ServantLauncher.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

---

### Шаг 3: Напишите код плагина
Реализуйте интерфейс `IServantPlugin`:

```csharp
using System;
using ServantLauncher.Models;
using ServantLauncher.Services.PluginSystem;

namespace MyServantPlugin
{
    public class MyAwesomePlugin : IServantPlugin
    {
        public string Id => "com.example.myplugin";
        public string Name => "Супер Плагин";
        public string Version => "1.0.0";
        public string Author => "Ваш Ник";
        public string Description => "Делает полезные действия перед запуском Minecraft.";

        private IPluginContext? _context;

        public void OnEnable(IPluginContext context)
        {
            _context = context;
            _context.Log(Id, "Плагин успешно активирован!");

            // Регистрация хука ПЕРЕД запуском сборки
            _context.RegisterPreLaunchHook(OnPreLaunch);

            // Регистрация хука ПОСЛЕ завершения игры
            _context.RegisterPostLaunchHook(OnPostLaunch);
        }

        private void OnPreLaunch(GameProfile profile)
        {
            _context?.Log(Id, $"Сборка '{profile.Name}' запускается на версии {profile.GameVersion}!");
            
            // Здесь можно, например:
            // - Очистить временные файлы
            // - Сделать бэкап папки saves
            // - Запустить кастомную фоновую музыку
        }

        private void OnPostLaunch(GameProfile profile, int exitCode)
        {
            _context?.Log(Id, $"Игра '{profile.Name}' закрылась с кодом: {exitCode}");
        }

        public void OnDisable()
        {
            _context?.Log(Id, "Плагин выключен.");
            _context = null;
        }
    }
}
```

---

### Шаг 4: Сборка и установка
1. Соберите проект:
   ```bash
   dotnet build -c Release
   ```
2. Скопируйте получившийся файл `MyServantPlugin.dll` в:
   ```
   %AppData%\.servantlauncher\plugins\
   ```
3. Откройте **Настройки лаунчера ➔ вкладка «Плагины»**. Ваш плагин сразу появится в списке, где его можно включать и отключать!

---

## 🧩 Возможности `IPluginContext`

Через переданный контекст вам доступны:
- `context.Profiles` — управление профилями сборок, доступ к папкам `.minecraft`, `mods`, `saves`.
- `context.Launcher` — состояние игры (`IsGameRunning`), принудительное завершение.
- `context.Themes` — смена темы, акцентных цветов, палитры и фона.
- `context.Accounts` — активный аккаунт игрока.
- `context.Progress` — отображение прогресс-бара снизу лаунчера при выполнении длительных операций.
- `context.RegisterPreLaunchHook` / `RegisterPostLaunchHook` — автоматизация событий игры.
