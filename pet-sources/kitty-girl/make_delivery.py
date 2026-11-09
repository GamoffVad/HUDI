from pathlib import Path
import json,html
ROOT=Path(__file__).parent
CASES=[('Осмотреться','look-around'),('Топать и злиться','angry-stomp'),('Нетерпеливо ждать','impatient-wait'),
 ('Нахмуриться и наклониться','indignant-lean'),('Хитро посмеяться','mischievous-giggle'),('Удивиться','surprised-step-v2'),
 ('Укоризненно посмотреть','stern-reproach'),('Крутить у виска','temple-circle'),('Постучать по голове','head-tap'),
 ('Потанцевать','playful-dance'),('Обернуться по сторонам','peek-both-shoulders'),('Походить в разные стороны','walk-directions')]
rows=[];cards=[]
for title,scene in CASES:
    p=ROOT/'animations'/scene;meta=json.loads((p/'export-manifest.json').read_text())
    assert meta['allInside'] and meta['firstLastIdentical']
    rows.append({'title':title,'scene':scene,'video':str(p/'final-preview.mp4'),'frames':str(p/'transparent-frames.zip'),**meta})
    cards.append(f'<article><h2>{html.escape(title)}</h2><video controls loop muted preload="metadata" src="animations/{scene}/final-preview.mp4"></video><a href="animations/{scene}/transparent-frames.zip">PNG-кадры · 1440×1440</a></article>')
(ROOT/'delivery.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
(ROOT/'review.html').write_text('''<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Девочка в розовом · 12 движений</title><style>body{font:16px system-ui;background:#15191c;color:#eef4f6;max-width:1300px;margin:32px auto;padding:0 20px}h1{font-size:28px}p{color:#b4c5cd}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,450px),1fr));gap:24px}article{background:#20272c;padding:16px}h2{font-size:18px;margin:0 0 12px}video{width:100%;display:block;margin-bottom:10px}a{color:#a8ddf4}</style><h1>Девочка в розовом</h1><p>12 циклов. Слева — светлая подложка, справа — тёмная. Превью полного разрешения после вырезания; в пакете HUDI дополнительно исправлен оттенок отдельных краевых пикселей волос. Первый и последний кадры совпадают.</p><main>'''+''.join(cards)+'</main></html>',encoding='utf-8')
(ROOT/'DELIVERY.md').write_text('''# Девочка в розовом

12 анимаций Kling 4.0. Выбран режим 1080p; фактические исходники сервиса — 1440×1440, 24 кадра/с. Полные RGBA-мастера сохранены. Для приложения кадры равномерно уменьшены до 960×960 без индивидуального кадрирования и изменения размера персонажа между кадрами.

[Просмотр всех циклов](D:/PR/HUDI/pet-sources/kitty-girl/review.html)

[Пакет HUDI](D:/PR/HUDI/output/kitty-girl-v1/kitty-girl.pet)

В пакете 1429 PNG и 12 действий. Общая начальная и конечная поза, сохранены тонкие волосы и контактная тень. В быстрых движениях остаётся естественная мягкость исходного видео. Дополнительная очистка цвета краевых пикселей волос в пакете не меняет альфа-канал.

Первый дубль удивления исключён из-за выхода волос за верхнюю границу исходника; используется проверенный второй дубль. Остальные 11 дублей сохранены.

Расход: 13 генераций × 16,25 = 211,25 кредита. Проверенный остаток — 41,8. Локальное вырезание и упаковка кредитов Higgsfield не расходуют.
''',encoding='utf-8')
print('Delivery index: 12 clips')
