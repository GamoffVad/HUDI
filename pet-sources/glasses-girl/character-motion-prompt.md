# Постоянное направление анимаций героини

Пожелание пользователя от 9 ноября 2026: героиня должна выглядеть живой во всех следующих анимациях. Использовать естественные шаги и движение всего тела, повороты в разные стороны, выразительную мимику и развевающиеся волосы. Выбирать движения по смыслу эмоции; не заставлять её механически выполнять все действия в каждом ролике.

Базовые требования чистой подложки, сохранения личности и полного кадра: `D:/PR/HUDI/docs/pet-generation-prompt.md`.

Добавка к каждому промпту движения:

> Give her lively, natural whole-body acting appropriate to this action. Eyes lead the head, shoulders and torso; changes of facing are supported by real foot pivots and weight transfers. When she walks, use alternating support legs, convincing heel-to-toe contact, relaxed arm swing and gentle hip/shoulder counter-rotation. Include motivated movement and turns rather than a frozen body with facial animation only. Long hair follows the body with flowing inertia and gravity, swings naturally during turns and settles after motion. Preserve identity, anatomy, proportions, camera distance and apparent size. Keep the complete motion envelope, including all hair and the soft underfoot contact shadow, inside every frame. Begin and end in the exact same supplied ordinary neutral pose, with natural settling. No motion blur or artificial feathering of opaque edges; preserve fine antialiasing.

Не добавлять старые ограничения «ступни всё время на полу» и «волосы двигаются только на несколько сантиметров» к сценам ходьбы и поворотов. Полный кадр обеспечивать достаточным пространством, а не замораживанием движения. Для этой героини лицо с ртом; правила безротого Инка не применяются.

Текущий нейтральный производственный кадр для новой серии: `animations/surprise-v3-kling/neutral-wide.png`. Возврат проверять по результату; при сборке сохранять единую геометрию холста, не подгонять масштаб каждого кадра отдельно.
