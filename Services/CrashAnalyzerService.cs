using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public enum CrashCategory
    {
        MissingDependency,
        ModConflict,
        OutOfMemory,
        JavaIncompatible,
        MixinCrash,
        GraphicsDriver,
        Unknown
    }

    public class CrashAnalysisResult
    {
        public CrashCategory Category { get; set; } = CrashCategory.Unknown;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? MissingModId { get; set; }
        public string? MissingModName { get; set; }
        public string? ProblematicModName { get; set; }
        public string? ProblematicModFile { get; set; }
        public string ActionButtonText { get; set; } = string.Empty;
        public bool CanAutoFix { get; set; }
    }

    public interface ICrashAnalyzerService
    {
        CrashAnalysisResult Analyze(string logText, GameProfile? profile);
    }

    public class CrashAnalyzerService : ICrashAnalyzerService
    {
        public CrashAnalysisResult Analyze(string logText, GameProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(logText))
            {
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.Unknown,
                    Title = "Неизвестный сбой",
                    Description = "Не удалось автоматически определить причину сбоя по логам."
                };
            }

            // 1. Missing Dependency (Fabric Loader / Quilt / Forge)
            // Pattern: Mod 'X' requires version ... of mod 'Y', which is missing!
            var missingDepMatch = Regex.Match(logText, @"(?:Mod\s+['""](?<mod>[^'""]+)['""](?:\s*\([^)]+\))?\s+requires\s+.*?of\s+(?:mod\s+)?['""](?<dep>[^'""]+)['""],?\s*which\s+is\s+missing)|(?:Could\s+not\s+find\s+required\s+mod:\s*['""]?(?<dep2>[a-zA-Z0-9_\-]+)['""]?)", RegexOptions.IgnoreCase);
            if (missingDepMatch.Success)
            {
                var depId = missingDepMatch.Groups["dep"].Success ? missingDepMatch.Groups["dep"].Value : missingDepMatch.Groups["dep2"].Value;
                var modName = missingDepMatch.Groups["mod"].Success ? missingDepMatch.Groups["mod"].Value : "";

                var cleanDepName = FormatModName(depId);
                var desc = !string.IsNullOrEmpty(modName)
                    ? $"Мод '{modName}' требует для работы обязательную библиотеку '{cleanDepName}' ({depId}), но она не установлена."
                    : $"Для работы сборки требуется обязательный мод/библиотека '{cleanDepName}' ({depId}).";

                return new CrashAnalysisResult
                {
                    Category = CrashCategory.MissingDependency,
                    Title = $"Отсутствует обязательная библиотека: {cleanDepName}",
                    Description = desc,
                    MissingModId = depId.Trim(),
                    MissingModName = cleanDepName,
                    ActionButtonText = $"Установить {cleanDepName} с Modrinth",
                    CanAutoFix = true
                };
            }

            // Pattern: Fabric API missing directly
            if (logText.Contains("requires fabric-api", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("fabric-api: which is missing", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("requires version of fabric", StringComparison.OrdinalIgnoreCase))
            {
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.MissingDependency,
                    Title = "Отсутствует Fabric API",
                    Description = "Установленным модам для запуска необходим Fabric API. Установите его в сборку в один клик.",
                    MissingModId = "fabric-api",
                    MissingModName = "Fabric API",
                    ActionButtonText = "Установить Fabric API с Modrinth",
                    CanAutoFix = true
                };
            }

            // 2. Mod Conflicts / Incompatibilities
            // Pattern: Mod 'X' is incompatible with mod 'Y'
            var conflictMatch = Regex.Match(logText, @"Mod\s+['""](?<mod1>[^'""]+)['""](?:\s*\([^)]+\))?\s+is\s+incompatible\s+with\s+(?:mod\s+)?['""](?<mod2>[^'""]+)['""]", RegexOptions.IgnoreCase);
            if (conflictMatch.Success)
            {
                var mod1 = conflictMatch.Groups["mod1"].Value;
                var mod2 = conflictMatch.Groups["mod2"].Value;
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.ModConflict,
                    Title = $"Конфликт несовместимых модов: {mod1} и {mod2}",
                    Description = $"Мод '{mod1}' не может работать одновременно с '{mod2}'. Рекомендуется отключить один из них.",
                    ProblematicModName = mod1,
                    ActionButtonText = $"Отключить {mod1}",
                    CanAutoFix = true
                };
            }

            // Duplicate mod ID
            var dupMatch = Regex.Match(logText, @"Duplicate\s+mod\s+id\s+found:\s*['""]?(?<dup>[a-zA-Z0-9_\-]+)['""]?", RegexOptions.IgnoreCase);
            if (dupMatch.Success)
            {
                var dupId = dupMatch.Groups["dup"].Value;
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.ModConflict,
                    Title = $"Дубликат мода: {dupId}",
                    Description = $"В папке mods найдено несколько версий одного и того же мода '{dupId}'. Удалите старую версию.",
                    ProblematicModName = dupId,
                    ActionButtonText = "Открыть папку mods",
                    CanAutoFix = false
                };
            }

            // 3. Out of Memory (OOM)
            if (logText.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("Java heap space", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("GC overhead limit exceeded", StringComparison.OrdinalIgnoreCase))
            {
                var currentRam = profile?.AllocatedRamMb ?? 4096;
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.OutOfMemory,
                    Title = "Нехватка оперативной памяти (OutOfMemory)",
                    Description = $"Игре не хватило выделенной памяти (текущее значение: {currentRam} MB) для загрузки всех модов, блоков и шейдеров.",
                    ActionButtonText = "Выделить +2 GB памяти",
                    CanAutoFix = true
                };
            }

            // 4. Java Version Incompatibility
            if (logText.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("has been compiled by a more recent version of the Java Runtime", StringComparison.OrdinalIgnoreCase))
            {
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.JavaIncompatible,
                    Title = "Несовместимая версия Java",
                    Description = "Моды или версия Minecraft были скомпилированы для более новой версии Java (например, Java 21 или Java 25).",
                    ActionButtonText = "Скачать Java 21 в Настройках",
                    CanAutoFix = false
                };
            }

            // 5. Mixin Critical Crash
            var mixinMatch = Regex.Match(logText, @"MixinApplyError.*?in\s+target\s+\((?<mixin>[a-zA-Z0-9_\-]+\.mixins?\.json)", RegexOptions.IgnoreCase);
            if (mixinMatch.Success)
            {
                var mixinConfig = mixinMatch.Groups["mixin"].Value;
                var modGuess = mixinConfig.Replace(".mixins.json", "").Replace(".mixin.json", "");
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.MixinCrash,
                    Title = $"Сбой миксина в моде: {modGuess}",
                    Description = $"Произошла внутренняя ошибка внедрения кода мода ({mixinConfig}). Попробуйте обновить или отключить этот мод.",
                    ProblematicModName = modGuess,
                    ActionButtonText = $"Отключить мод {modGuess}",
                    CanAutoFix = true
                };
            }

            // 6. Graphics Driver / OpenGL
            if (logText.Contains("GLFW error 65542", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("OpenGLException", StringComparison.OrdinalIgnoreCase) ||
                logText.Contains("WGL: The driver does not appear to support OpenGL", StringComparison.OrdinalIgnoreCase))
            {
                return new CrashAnalysisResult
                {
                    Category = CrashCategory.GraphicsDriver,
                    Title = "Сбой видеодрайвера (OpenGL)",
                    Description = "Драйвер вашей видеокарты не поддерживает OpenGL или поврежден. Рекомендуется обновить драйверы графики.",
                    ActionButtonText = "Понятно",
                    CanAutoFix = false
                };
            }

            return new CrashAnalysisResult
            {
                Category = CrashCategory.Unknown,
                Title = "Неизвестная ошибка",
                Description = "Ознакомьтесь с логом или скопируйте его для обращения в поддержку."
            };
        }

        private static string FormatModName(string modId)
        {
            return modId switch
            {
                "fabric-api" or "fabric" => "Fabric API",
                "cloth-config" or "cloth-config2" => "Cloth Config",
                "architectury" or "architectury-api" => "Architectury API",
                "indium" => "Indium",
                "sodium" => "Sodium",
                "iris" => "Iris Shaders",
                "yet_another_config_lib_v3" or "yacl" => "Yet Another Config Lib (YACL)",
                "modmenu" => "Mod Menu",
                _ => modId
            };
        }
    }
}
