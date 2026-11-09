# Девушка в очках

Готовый пакет с девятью анимациями: [Glasses-Girl-3.pet](https://github.com/GamoffVad/HUDI/releases/download/v7.3.10/Glasses-Girl-3.pet).

В этой папке сохранены принятые исходные видео, промпты, общий референс и обработка альфа-канала. Исходник раннего удивления с обрезанными волосами исключён; сохранён целый ранний дубль `surprise-v1` и уже выбранный основной цикл `surprise-v3-kling`.

Для повторной подготовки нужны Python, Pillow, numpy, scipy, opencv-python и FFmpeg. Укажите путь к FFmpeg в `HUDI_FFMPEG` либо добавьте его в PATH. Запустите `recut_animations_v3.py` с именами восьми сцен (все папки animations кроме surprise-v1), затем `tools/prepare-glasses-girl.py` из корня репозитория и `tools/add-glasses-girl-classic.py` для девятого действия. Полученные PNG и пакеты сохраняются локально в output.

Хеши исходников: [provenance.json](provenance.json). Требования к движению: [character-motion-prompt.md](character-motion-prompt.md).
