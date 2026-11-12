# Двойник — Операция «Сигнал»

Учебная игра от первого лица на Unity. Командир Алекс восстанавливает связь с Землёй после абордажа. Кампания состоит из трёх сцен. Игрок может управлять персонажем с клавиатуры и мыши, русскими голосовыми командами и жестами через веб-камеру.

- `Assets/ZeroSector/Scripts` — игровая логика, интерфейс и ввод.
- `Assets/DoppelgangerGenerated/Campaign` — сцены кампании.
- `Assets/Resources/Doppelganger/Radio` — диалоги и подтверждения команд.
- `Assets/StreamingAssets` — локальные зависимости распознавания речи и жестов. Архив `hand-worker.zip` хранится через Git LFS.

Для работы с проектом нужна Unity **6000.6.7f1**. После клонирования выполните `git lfs pull`. [Руководство по кампании](CAMPAIGN_README.md) описывает управление и сборку.

Для запуска всех сцен на чистом клоне импортируйте из Unity Asset Store наборы **ADAM Character Pack**, **3D Scifi Kit Starter Kit HD (URP)**, **Sci-Fi Styled Modular Pack** и **Easy FPS**. Исходники этих наборов и подготовленные из ADAM ассеты хранятся локально. Названия и условия использования приведены в [уведомлениях о сторонних материалах](THIRD_PARTY_NOTICES.md).

После импорта ADAM в открытом редакторе вне Play Mode запишите `adam-art` в файл `Temp/DoppelgangerChecks/authoring-command.txt`. Дождитесь `adam-art: completed` в `authoring-result.txt`, затем назначьте созданный `Assets/DoppelgangerGenerated/Characters/AdamRobot.prefab` в поле `Enemy Prefab` компонента `CampaignGame` в каждой сцене кампании и сохраните сцены. Для восстановления анимации охраны и пролога выполните в Unity методы `ZeroSector.Editor.CombatMotionAuthoring.Prepare` и `ZeroSector.Editor.ImpactPrologueAuthoring.Prepare` в указанном порядке.
