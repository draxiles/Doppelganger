# Сторонние материалы Doppelgänger

## Интерфейс

Golos Text (Regular, Bold, Black) от The Golos Text Project Authors. [Репозиторий](https://github.com/googlefonts/golos-text). Лицензия SIL Open Font License 1.1 находится в `Assets/Resources/Doppelganger/Interface/Golos-OFL.txt`.

## ADAM Character Pack — робот

Использован настоящий робот ADAM из импортированного пользователем официального пакета Unity Technologies: `Assets/UnityTechnologies/Adam Character Pack`. Источник: https://assetstore.unity.com/packages/3d/characters/adam-character-pack-characters-74842

**Restricted Asset: личное и некоммерческое использование.** Точные условия находятся в `Assets/UnityTechnologies/Adam Character Pack/license.txt`. Этот персонаж используется в учебной демонстрации. Для коммерческого выпуска необходимо заменить его персонажем с подходящей лицензией или получить отдельное разрешение. Наличие модели коммерциализации проекта само по себе не даёт права продавать материалы ADAM.

Сохранены исходная модель, материалы и ходьба Adam_Walk; для нашей игры создана отдельная оболочка управления и анимации, приседа, попадания и падения.

## Unity FPS Sample — выбранные графические материалы

Источник: https://github.com/Unity-Technologies/FPSSample

Copyright Unity Technologies. Лицензия выбранных материалов: Unity Companion License; копия исходного уведомления сохранена вместе с импортом: `Assets/ThirdParty/UnityFpsSample/LICENSE.md`.

Использованы модели Terraformer/Robot от первого лица, два оружия, их текстуры и клипы. Для общего облика игрока и Двойника добавлены `Terraformer_A.fbx`, `Terraformer_Weapon_A_3P.fbx` и карты Terraformer_A с разрешением до 2048 px. Отдельные клипы Terraformer из `Assets/Animation/Characters/Terraformer/Clips` дают боевую стойку, четыре направления бега, спринт, прицеливание и выстрел. Эти клипы используются с игровой логикой Doppelgänger, собственными prefab и материалами URP. Исходная игра, её скрипты и сцены не перенесены. Unity Companion License рассчитана на использование с Unity; материалы не следует считать универсальным набором для любых движков.

## Лицо Алекса — MakeHuman Community

В `Assets/ThirdParty/MakeHumanVetrov/` находятся адаптированные меши головы и стрижки на основе `base.obj` (hm08), таргета `caucasian-male-young`, модели `short01` и карты кожи `middleage_caucasian_male`. Использованные графические исходники распространяются по CC0 1.0. Скрипт сборки формы и привязки волос находится в `ThirdPartyStaging/VetrovAppearance/build_head.py`. Код приложения MakeHuman в игре не используется. [Лицензия исходников](https://github.com/makehumancommunity/makehuman/blob/master/LICENSE.md) · [Каталог ассетов CC0](https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html).

## Земля — Solar System Scope / INOVE

Текстуры: Solar System Scope, https://www.solarsystemscope.com/textures/

Creative Commons Attribution 4.0 International: https://creativecommons.org/licenses/by/4.0/

Файлы: `8k_earth_daymap.jpg`, `2k_earth_nightmap.jpg`, `8k_earth_clouds.jpg`. Исходные изображения сохранены; в Unity настроены разрешение импорта, материалы и собственные шейдеры освещения, облаков и атмосферы. Уведомление также находится в `Assets/DoppelgangerGenerated/Earth/Textures/LICENSE.txt`. При публикации игры указать эти сведения в разделе авторов.

## Наборы, импортированные пользователем через Asset Store

- Creepy Cat — 3D Scifi Kit Starter Kit HD: модульный интерьер и его текстуры.
- Sci-Fi Styled Modular Pack: отдельная мебель и материалы, прежние тестовые сцены.
- Easy FPS: сохранённый набор и звук выстрела; основные модели оружия новых сцен заменены на Unity FPS Sample.

Эти наборы остаются под своими лицензиями Asset Store и уведомлениями авторов. Собственные копии материалов для URP не меняют лицензии исходных ресурсов. Не распространять исходные наборы как самостоятельный продукт. Перед коммерческим релизом проверить условия конкретных полученных пользователем версий и дополнить титры.

## Vosk — локальное распознавание русских команд

Copyright Alpha Cephei Inc. Используется официальный нативный Vosk API 0.3.45 для Windows x64; C#-адаптер ввода и игровая интеграция написаны для этого проекта. Python или облачный сервер для запуска игры не требуются.

- Проект и C API: https://github.com/alphacep/vosk-api
- Выпуск: https://github.com/alphacep/vosk-api/releases/tag/v0.3.45
- Нативный комплект: https://github.com/alphacep/vosk-api/releases/download/v0.3.45/vosk-win64-0.3.45.zip
- Русская модель: https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip
- Авторский каталог моделей и условия модели: https://alphacephei.com/vosk/models

**Vosk и vosk-model-small-ru-0.22: Apache License 2.0.** Копия лицензии из выпуска Vosk сохранена в `Assets/StreamingAssets/Voice/APACHE-2.0.txt`. Модель не переобучалась и поставляется в исходном виде. Ограниченная грамматика русских игровых команд задаётся при создании распознавателя.

Из официального Windows-комплекта используются `libvosk.dll`, `libgcc_s_seh-1.dll`, `libstdc++-6.dll`, `libwinpthread-1.dll` в `Assets/Plugins/x86_64/`. Библиотеки GCC runtime сопровождаются GPLv3 и GCC Runtime Library Exception 3.1; тексты сохранены в `Assets/StreamingAssets/Voice/GPL-3.0.txt` и `GCC-RUNTIME-EXCEPTION.txt`. Официальные тексты GCC: https://github.com/gcc-mirror/gcc/blob/master/COPYING3 и https://github.com/gcc-mirror/gcc/blob/master/COPYING.RUNTIME.

Сведения об источниках и SHA-256 загруженных архивов включены в `Assets/StreamingAssets/Voice/SOURCES.txt` и копируются со StreamingAssets в Windows-сборку. Нативный архив: `F1DCC9CCA460630F81EA8F71794F69C80BED6556D2A4E6237B5785E1D2DFF34B`; архив модели: `961D5FF98A17F4AA6DE69864D0AA71FA5BAC682301D2B5D17A3F24C5C99A46D4`.

Тестовые русские WAV в `ThirdPartyStaging/VoskRussian/TestAudio/` синтезированы установленным локальным голосом Microsoft Irina исключительно для проверки распознавания. Это тестовые данные, не реплики персонажей игры и не запись микрофона пользователя; в игровую сборку эти WAV не включаются.

Реплики Геры нарезаны из предоставленной записи MiniMax. Реплики Алекса и ADAM синтезированы локально с помощью двух голосовых состояний xVibePocketTTS от GenVoice. Код xVibePocketTTS опубликован под MIT. Базовые веса Kyutai Pocket TTS опубликованы под CC BY 4.0. Игра использует готовые записи WAV. [xVibePocketTTS](https://github.com/GenVoice/xVibePocketTTS) · [Веса и сведения об обучении](https://huggingface.co/genvoice/xVibePocketTTS) · [Pocket TTS](https://huggingface.co/kyutai/pocket-tts)

## MediaPipe — распознавание кистей и жестовые команды

Copyright The MediaPipe Authors / Google. Используется официальный **MediaPipe 0.10.32 Hand Landmarker**, Windows x64 CPU, и неизменённый обученный комплект `hand_landmarker.task` (float16, version 1). Модель определяет ладони, 21 ориентир каждой кисти, трёхмерные координаты и принадлежность левой/правой руке. Поверх реальных результатов ML написан собственный открытый классификатор поз по углам пальцев и расстояниям; переобучение модели на игровых жестах не заявляется. Камера больше не подменяет жесты движением в трёх зонах.

- Исходники и Apache License 2.0: https://github.com/google-ai-edge/mediapipe
- Python API и модель: https://ai.google.dev/edge/mediapipe/solutions/vision/hand_landmarker/python
- Модель: https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task
- Выпуск библиотеки: https://pypi.org/project/mediapipe/0.10.32/

Автономный процесс поставляется как `Assets/StreamingAssets/HandGestures/hand-worker.zip`: MediaPipe, Python 3.12.14, NumPy, необходимые зависимости изображений и загрузчик PyInstaller 6.16.0. Python устанавливать пользователю не требуется. Первый запуск F8 распаковывает этот архив в отдельный каталог `HandGestures` внутри каталога данных игры. DLL из архива не импортируются Unity как плагины. Распознавание работает локально, без ключа, сервера и отправки видео в сеть. Процесс получает кадры только от владельца камеры в Unity через ограниченный двоичный канал stdin/stdout; самостоятельно устройства не открывает.

Лицензии MediaPipe (Apache 2.0), Python (PSF), NumPy (BSD), OpenCV (Apache 2.0 и уведомления зависимостей), Pillow (MIT-CMU), Matplotlib и остальных включённых зависимостей сохранены в `Assets/StreamingAssets/HandGestures/LICENSES/`. Лицензия PyInstaller и исключение для загрузчика сохранены там же. Источники, версии и SHA-256 модели/архива находятся в `SOURCES.txt`. Собственный исходник процесса приложен как `hand_worker-source.py.txt`; исходники сборки и тестов находятся в `ThirdPartyStaging/HandGestures/`.

Проверочные фотографии из официального примера MediaPipe (источник Pixabay, ссылки указаны в исходном notebook) сохранены исключительно в `ThirdPartyStaging/HandGestures/test-images/`. В игровую сборку фотографии не включаются. Они используются для проверки настоящего вывода модели, а синтетические координаты — для проверки полного набора игровых соответствий. Эти проверки не являются испытанием физической вебкамеры пользователя.
