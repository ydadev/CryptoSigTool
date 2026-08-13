# Сборка проекта

## Среда

- Windows 10/11 x64;
- .NET SDK 8 или новее;
- PowerShell 5.1 или новее;
- доступ к NuGet для первой автономной публикации.

## Структура

- `CryptoSigTool/` — основное WinForms-приложение;
- `CryptoSigTool.Installer/` — системный Windows-установщик и деинсталлятор для всех пользователей;
- `tools/IconBuilder/` — детерминированное создание многоразмерной `.ico` из PNG;
- `docs/` — пользовательская и техническая документация;
- `build-release.ps1` — автономная публикация, упаковка и SHA-256.

## Отладочная сборка

```powershell
dotnet build .\CryptoSigTool\CryptoSigTool.csproj -c Debug
```

## Релиз

```powershell
.\build-release.ps1 -Version 1.4.0
```

Сценарий публикует приложение и установщик как self-contained `win-x64`, создаёт ZIP и `SHA256SUMS.txt`. Каталоги `artifacts/` и `dist/` являются результатом сборки и не коммитятся.

## Проверки перед релизом

1. Запустить `CryptoSigTool.exe --ui-smoke`.
2. Запустить `CryptoSigTool-Setup.exe --installer-smoke`.
3. Проверить реальную отсоединённую подпись через CryptoPro.
4. На тестовом компьютере запустить установщик с UAC и установить в `%ProgramFiles%\CryptoSigTool`.
5. Убедиться, что CryptoSigTool появился в «Установленных приложениях».
6. Удалить приложение штатным деинсталлятором с правами администратора и проверить отсутствие файлов/общих ярлыков.

