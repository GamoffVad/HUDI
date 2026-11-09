# Девочка в розовом

[Готовый пакет · 12 анимаций](https://github.com/GamoffVad/HUDI/releases/download/v7.3.10/Kitty-Girl-1.pet) · [Превью циклов](https://github.com/GamoffVad/HUDI/releases/download/v7.3.10/Kitty-Girl-Previews.zip)

Модель Kling 4.0, запрос 1080p; получены исходники 1440×1440, 24 кадра/с. В пакет для приложения входят 1429 RGBA PNG 960×960. Первый и последний кадры каждой сцены совпадают с общей позой. Полный холст и положение персонажа сохраняются при обработке.

Промпты: [основные сцены](PROMPTS.md), [дополнительные сцены](ADDITIONAL-PROMPTS.md). Удивление заменено дублем `surprised-step-v2`, в котором волосы полностью помещаются в кадре.

Для локальной сборки нужны Python, Pillow, numpy, scipy, opencv-python и FFmpeg. Укажите FFmpeg в переменной `HUDI_FFMPEG` или PATH.

1. Выполните `process_video.py` с именами нужных папок из animations; начните с look-around.
2. Выполните `finalize.py` для тех же сцен. Проверка отклоняет выход за границы и существенно отличающиеся крайние позы.
3. Из корня репозитория выполните `python tools/prepare-kitty-girl.py`. Пакет появится в output/kitty-girl-v1.

Пример: `python pet-sources/kitty-girl/process_video.py look-around angry-stomp`. Остальные имена перечислены в `tools/prepare-kitty-girl.py`. Мастера и промежуточные кадры генерируются локально; видео-исходники хранятся в Git LFS.
