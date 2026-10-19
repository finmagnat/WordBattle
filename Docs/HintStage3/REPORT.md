# Hint — этап 3

Реализация в репозитории завершена. Публикация CloudScript и Title Data в PlayFab не выполнялась. Реальные покупки, показы AdMob и production grants не выполнялись. Этап 4 не начат.

Полный список изменённых и добавленных файлов с абсолютными путями: `changed-files.txt`. Сравнение исходной экономики с HEAD до этапа: `economy-baseline-report.txt`.

**Состав наград и вероятности**

| Source | Booster | Amount | Weight / Chance |
|---|---|---:|---|
| Paid `wordlet_pack_best` | Letter, Slowdown, Eraser, Mixer, Swap, **Hint** | по 5 | фиксированный состав |
| Individual paid packs | прежний единственный booster | 10 | без изменений; Hint в них не добавлен |
| Shop rewarded | Letter | 1 | отдельное предложение; 2/день, cooldown 30 с |
| Shop rewarded | Slowdown, Eraser, Mixer, Swap, **Hint** | по 1 | отдельное предложение каждого типа; 3/день, cooldown 45 с |
| Daily, дни 1–5 | Letter / Slowdown / Eraser / Swap / Mixer | 1 | фиксированный соответствующий день |
| Daily, день 6 | Eraser + **Hint** | по 1 | фиксированный день; прежний Eraser сохранён |
| Daily, день 7, обычный drop | Letter, Eraser, Slowdown, Mixer, Swap, **Hint** | 2 выбранного | weight 70; каждый тип 1/6 внутри pool; 11,6667% на конкретный тип среди всех открытий сундука |
| Daily, день 7, усиленный drop | те же 6 | 3 выбранного | weight 20; каждый тип 1/6; 3,3333% на конкретный тип среди всех открытий |
| Daily, день 7, jackpot | все 6 | по 1 | weight 10; 10% |
| Starter | Letter, Slowdown, Eraser, Mixer, Swap, **Hint** | по 1 | один раз по прежнему `starter_bonus_state=granted` |
| Win Streak | Letter, Slowdown, Eraser, Swap, **Hint** | 1 выбранного | каждый weight 1, каждый 20%; порог 3 победы прежний |
| End Round Rewarded | Letter / Slowdown / Eraser / Mixer / Swap / **Hint** | 1 выбранного | weights **1 / 2 / 3 / 4 / 5 / 3**; проценты **5,5556 / 11,1111 / 16,6667 / 22,2222 / 27,7778 / 16,6667** |

Магазинная реклама не использует случайный pool: игрок выбирает предложение нужного типа. Daily использует существующие списки `Pool` с равномерным выбором; один элемент Hint имеет тот же неявный вес 1, что остальные элементы. Число повторений в списке позволяет менять относительный вес существующим способом. Шансы верхнего уровня сундука 70/20/10 сохранены. Суммарный шанс получить Hint из сундука — 25% с учётом jackpot.

Для End Round стандартного веса обычного бустера не было: прежние значения различались. Начальный вес Hint выбран **3** и остаётся редактируемым в `AdsConfig`. Это параметр для дальнейшей настройки экономики. Win Streak переведён с равномерного списка на список с явным весом: прежним четырём записям сохранён вес 1. Mixer в этом источнике прежде отсутствовал и не добавлялся.

**Магазин и серверная выдача**

Hint добавлен только в существующий общий продукт **`wordlet_pack_best` ×5**. Цены, валюты, Google Play product IDs, названия продуктов и количества остальных бустеров сохранены. Особенность старого каталога: **`wordlet_pack_hint_10` по-прежнему выдаёт Letter ×10**, а не новый Hint.

Android раньше начислял состав локально, подтверждал покупку и затем вызывал отдельные AddBooster. Теперь он передаёт чек в общий `GrantPack`, ждёт проверку/серверный состав/синхронизацию и только затем подтверждает покупку. Отдельного клиентского AddHint нет. Ошибка сохраняет покупку pending. Одновременные и повторные callbacks одной покупки не начинают повторную клиентскую выдачу. Dev shop использует тот же gateway и уникальный transaction ID; прежний серверный debug gate сохранён.

Старый `server.ValidateGooglePlayPurchase` не является поддерживаемым Server API. Исправление вызывает HTTPS Client API из CloudScript, предварительно проверяя владельца session ticket через `server.AuthenticateSessionTicket`. Адрес использует доверенный `script.titleId`; новый ключ Title ID не требуется. HTTP logging отключён, ticket не сохраняется в пользовательских данных или ответе, Google-запрос не зеркалируется в PlayStream. Имена клиентских событий аналитики сохранены. Основание: [валидация Google purchase](https://learn.microsoft.com/en-us/rest/api/playfab/client/platform-specific-methods/validate-google-play-purchase?view=playfab-rest), [проверка сессии](https://learn.microsoft.com/en-us/rest/api/playfab/server/authentication/authenticate-session-ticket?view=playfab-rest), [глобальные объекты и HTTP CloudScript](https://learn.microsoft.com/en-gb/xbox/playfab/live-service-management/service-gateway/automation/cloudscript/writing-custom-cloudscript).

`GrantPack` берёт награды исключительно из серверного `shop_rewards`. Маркер `purchase_tokens_v2` записывается вместе с балансами. Google identity привязан к purchase token, поэтому изменение присланного transaction ID не повторяет выдачу. Старые маркеры `purchase_tokens` учитываются. `shop_validated_receipts` хранит подтверждение проверенного, но ещё не выданного чека на случай ошибки записи баланса; повтор выдачи не требует повторной проверки одноразового чека. Сам по себе `ReceiptAlreadyUsed` не разрешает начисление.

В rewarded shop успешный результат теперь означает завершённую выдачу, а не начало показа рекламы. При отмене/ошибке запуска RewardPopup не показывается. В обоих рекламных источниках повтор earned callback закрыт до первого асинхронного начисления. Placement, Ad Unit, существующие лимиты и частота рекламы не менялись.

**Что опубликовать в PlayFab**

1. Сначала на тестовом title сверить текущие опубликованные настройки с проектными. Экспорт из репозитория не доказывает совпадения production Title Data.
2. Опубликовать новую revision `Assets/Scripts/CloudScripts/CloudScript.js`, затем назначить её Live: изменения `DailyBonusBoosterKeys`, нормализации Hint, Starter Gift и `GrantPack` должны попасть в одну revision.
3. В Primary Title Data **`shop_rewards`** добавить **`wordlet_pack_best.boost_hint = 5`**, сохранив все прежние значения и неизвестные репозиторию продукты. `PlayFab/shop_rewards.merge-patch.json` — добавочная правка, **не полная замена Title Data**. `shop_rewards.reference.json` — справка по известным клиентскому каталогу продуктам.
4. Обновить Primary Title Data **`daily_bonus_config`** по `PlayFab/daily_bonus_config.json`: день 6 Eraser1 + Hint1; в обоих pools дня 7 добавить Hint; jackpot добавить Hint1. Сверить сохранение текущих серверных дней, количеств, weights и reset semantics перед публикацией. Asset DailyBonusConfig — authoring template, runtime читает серверный JSON.
5. Проверить существующую Google Play integration PlayFab и Classic Catalog: соответствующие ItemId должны совпадать с уже существующими Google product IDs, поскольку API валидации требует соответствия каталогу. Проверить отсутствие параллельного правила PlayStream, повторно увеличивающего те же `boost_*` balances. Создание новых Google Play продуктов или изменение цен не требуется.
6. Сначала проверить license tester / internal track на тестовом title: валидный чек, недоступный сервер, повтор callback, повтор запуска и синхронизацию всех шести балансов. Только затем выпускать соответствующий Android-клиент.

Серверный ключ остаётся **`boost_hint`**. Инициализации баланса старым игрокам не требуется. Уже получивший Starter Bonus игрок с отсутствующим/нулевым Hint не получает Starter повторно.

**UI и локализация**

Использованы прежний BoosterHint, generic ShopItemView, RewardPopup и Daily Bonus UI. В Starter добавлен шестой существующий item prefab и описание RU «Подсказка», UK «Підказка», EN «Hint». В TMP icon pack подключён fallback sprite `hint`, использующий ту же картинку. Размер текста Starter автоматически подстраивается в пределах 24–30. Пустой Hint теперь открывает обычный магазин.

Визуально проверены магазинный набор из шести предметов, RewardPopup, шестой день Daily и Starter в Blue/Wood/Paper. Starter также проверен в RU/UK/EN на 320×568. Скриншоты собраны в `Previews/*all-skins.png`. Это изолированные редакторские previews реальных prefab/config, без запуска production economy. Цена в preview магазина — значение DebugPriceText; фактическую локализованную цену предоставляет store.

При проверке выявлена несовместимая настройка двух существующих динамических Nunito Cyrillic fonts: atlas был non-readable. Включён Read/Write для их atlas textures, чтобы TMP мог дополнять символы без предупреждений. Остальные случайно прогретые шрифты не входят в изменение.

**Аналитика и совместимость**

Автоматически проверены Hint в purchase reward contents, rewarded reward type и фактических параметрах generic RewardPopup (`reward`, `source`). Daily/Starter/Win Streak проверены по существующим generic путям: server DTO содержит Hint, inventory сериализует enum, popup получает список наград; отдельного Hint event нет. Существующие названия событий сохранены. Grant не выполняется при повторной подготовке popup.

BoosterType numeric IDs, существующие inventory keys, форматы Manual Save / Auto Recovery, HintState и механика Hint не менялись. Одна устаревшая проверка этапа 1, ожидавшая исключения Hint из rewarded catalog, обновлена под требование этапа 3; количество прежних проверок сохранено.

**Проверки**

**Прошли 207 проверок: 146 прежних + 30 новых Unity economy/UI/analytics + 31 offline CloudScript.** Две Unity-сессии: 158 + 18 = 176; production saves unchanged — True. Компиляция успешна; финальные UI previews выполняются без ошибок и предупреждений.

Полный журнал находится в `unity-test-report.md` и `cloudscript-test-report.txt`. Запуск Unity: меню `Tools → Tests → Run isolated Auto Recovery Play Mode suite`. Запуск серверных тестов: `node Tests/hint-economy-cloudscript.cjs`.

Unity использует отдельную сцену, test inventory / store gateway / ad adapters / analytics recorder. Серверные тесты исполняют настоящий CloudScript в Node VM с fake server/http. Не выполняются Google Play purchase, AdMob show, production PlayFab calls. Production saves проверяются fingerprint до и после двух реальных Stop/Play сессий.

**Ручной checklist после серверной публикации**

- [ ] SHOP: открыть общий пак; увидеть все шесть иконок ×5; проверить локальную цену; license tester purchase даёт весь server payload один раз.
- [ ] SHOP REWARDED: выбрать Hint; отмена/ошибка не выдаёт награду; успешный просмотр даёт Hint1, один popup и один расход лимита; повтор callback не начисляет снова.
- [ ] DAILY: тестовым состоянием открыть день 6, Hint random drop и jackpot; сверить серверный и UI balance; повтор claim в тот же день запрещён.
- [ ] STARTER: новый тестовый аккаунт получает все шесть ×1; перезапуск не выдаёт повторно; старый granted аккаунт с Hint0 не получает новый Starter.
- [ ] WIN STREAK: тестово оставить только Hint с положительным weight, приблизить счётчик к 3; получить Hint1, reset0; повтор popup не начисляет снова; вернуть исходные weights.
- [ ] END ROUND: тестово оставить Hint в AdsConfig; успешный просмотр даёт Hint1 и один popup; cancel/failure/повтор callback не дают второй grant; вернуть исходные weights.
- [ ] Проверить RU/UK/EN и три skins на устройстве, включая шестую иконку и короткий экран.
- [ ] Проверить purchase/reward popup/rewarded/daily/starter/streak analytics: значение Hint, исходные event names и отсутствие дублирующих событий выдачи.

**Оставшиеся ограничения и параметры**

- Реальные опубликованные PlayFab настройки, Google credentials и покупки не проверялись. Android build и store sandbox остаются в ручной проверке перед выпуском; изменения версий SDK/нативных зависимостей не выполнялись.
- Вес Hint3 после раунда и добавление Hint1 к шестому дню — явно заданные начальные параметры, которые можно пересмотреть отдельно. Прежние количества/веса не перераспределялись.
- Сохранена ограниченная история последних 50 обработанных покупок. Это защита повторной доставки в существующей модели, не постоянный финансовый ledger. Legacy CloudScript read/modify/write не даёт распределённой транзакции между внешней валидацией и всеми видами wallet grants. Если процесс оборвётся сразу после успешной внешней проверки, до записи server proof, требуется разбор pending purchase по чеку; `ReceiptAlreadyUsed` намеренно не принимается без доказательства. Для строгой постоянной идемпотентности и конкурентных операций нужна отдельная серверная транзакционная модель.
- Starter quantities по-прежнему заданы серверной функцией; отдельного существующего starter config в проекте не было. Большой рефактор экономики не выполнялся.

**Letter availability в этапе 3 не изменялась.** Ни один источник Letter не удалён, цены/количества/raw weights сохранены. Абсолютные проценты random pools меняются только из-за допустимой нормализации после добавления Hint. Этап 4 не начат.
