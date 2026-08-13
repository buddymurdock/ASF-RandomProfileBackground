# ASF-RandomProfileBackground

Плагин для **[ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)**, который через случайные, довольно длинные интервалы (недели-месяцы) экипирует боту случайный фон профиля (или мини-фон) — но только из того, что бот **уже реально получил** сам, крафтя бейджи Steam Trading Cards. Никакого стороннего контента: если бот ничего не крафтил, экипировать нечего.

## Как это работает

У каждого бота свой независимый цикл: раз в случайное число дней в диапазоне `[MinDelayDays; MaxDelayDays]` плагин запрашивает у Steam список реально принадлежащих боту фонов профиля (`IPlayerService/GetProfileItemsOwned`) и мини-фонов, выбирает случайный из них и экипирует тем же публичным API (`IPlayerService/SetProfileBackground` / `SetMiniProfileBackground`), которым пользуется сам сайт steamcommunity.com. Один и тот же предмет подряд два раза не выбирается (если есть из чего выбрать).

Никнейм/аватар/статус других плагинов серии ([RandomNickname](https://github.com/buddymurdock/ASF-RandomNickname), [RandomProfileAvatar](https://github.com/buddymurdock/ASF-RandomProfileAvatar), [RandomOnlineStatus](https://github.com/buddymurdock/ASF-RandomOnlineStatus)) не затрагиваются.

## Установка

1. Скачайте архив плагина из [Releases](../../releases) и распакуйте в папку `plugins` рядом с ASF (создайте подпапку с именем плагина).
2. Перезапустите ASF.

## Конфигурация

Настройки задаются **глобально**, в `ASF.json`, как дополнительные (нераспознанные ASF) свойства верхнего уровня:

```json
{
	"RandomProfileBackgroundEnabled": true,
	"RandomProfileBackgroundMinDelayDays": 14,
	"RandomProfileBackgroundMaxDelayDays": 60
}
```

| Свойство | Тип | По умолчанию | Описание |
| --- | --- | --- | --- |
| `RandomProfileBackgroundEnabled` | `bool` | `false` | Включает/выключает плагин. |
| `RandomProfileBackgroundMinDelayDays` | `ushort`, дни | `14` | Нижняя граница случайной паузы между сменами фона. |
| `RandomProfileBackgroundMaxDelayDays` | `ushort`, дни | `60` | Верхняя граница случайной паузы между сменами фона. |

Если `MinDelayDays` больше `MaxDelayDays`, значения меняются местами автоматически. Если у бота нет ни одного собственного фона/мини-фона (ничего не накрафчено), плагин один раз пишет предупреждение и ничего не делает.

## Сборка

Проект использует **[ASF-PluginTemplate](https://github.com/JustArchiNET/ASF-PluginTemplate)** и собирается вместе с исходниками ASF, подключёнными как git submodule:

```sh
git clone --recurse-submodules https://github.com/buddymurdock/ASF-RandomProfileBackground.git
cd ASF-RandomProfileBackground
dotnet build -c Release
```

Если репозиторий уже склонирован без `--recurse-submodules`, подтяните submodule отдельно:

```sh
git submodule update --init --recursive
```

## Лицензия

Apache-2.0, см. [LICENSE.txt](LICENSE.txt).
